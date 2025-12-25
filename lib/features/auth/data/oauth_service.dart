import 'dart:async';
import 'dart:convert';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:http/http.dart' as http;
import 'package:url_launcher/url_launcher.dart';

import '../../../core/constants/app_constants.dart';

final oauthServiceProvider = Provider<OAuthService>((ref) {
  return OAuthService();
});

/// Result of a successful OAuth authentication.
class OAuthResult {
  final String accessToken;
  final String tokenType;
  final List<String> scopes;

  OAuthResult({
    required this.accessToken,
    required this.tokenType,
    required this.scopes,
  });
}

/// Device code information for the user to complete authorization.
class DeviceCodeInfo {
  final String deviceCode;
  final String userCode;
  final String verificationUri;
  final int expiresIn;
  final int interval;

  DeviceCodeInfo({
    required this.deviceCode,
    required this.userCode,
    required this.verificationUri,
    required this.expiresIn,
    required this.interval,
  });
}

/// Callback for device flow progress updates.
typedef DeviceFlowCallback = void Function(DeviceFlowStatus status, String? message);

enum DeviceFlowStatus {
  requestingCode,
  waitingForUser,
  polling,
  success,
  error,
  expired,
  cancelled,
}

class OAuthService {
  /// Current flow ID - each new flow gets a unique ID.
  /// Cancellation checks this ID to ensure only the current flow is affected.
  int _currentFlowId = 0;

  /// Starts the GitHub Device Flow for authentication.
  ///
  /// [onStatusChange] is called with status updates during the flow.
  /// Returns the access token on success, throws on failure.
  Future<OAuthResult> startDeviceFlow({
    required DeviceFlowCallback onStatusChange,
  }) async {
    // Increment flow ID to invalidate any previous flows
    final flowId = ++_currentFlowId;

    try {
      // Step 1: Request device code
      onStatusChange(DeviceFlowStatus.requestingCode, null);
      final deviceCodeInfo = await _requestDeviceCode();

      // Step 2: Open browser for user to enter code
      onStatusChange(DeviceFlowStatus.waitingForUser, deviceCodeInfo.userCode);

      final verificationUrl = Uri.parse(deviceCodeInfo.verificationUri);
      // Use platformDefault - works better with Flatpak portals
      // Skip canLaunchUrl check as it may return false in sandboxed environments
      await launchUrl(verificationUrl, mode: LaunchMode.platformDefault);

      // Step 3: Poll for access token
      onStatusChange(DeviceFlowStatus.polling, deviceCodeInfo.userCode);
      final result = await _pollForToken(
        deviceCodeInfo: deviceCodeInfo,
        onStatusChange: onStatusChange,
        flowId: flowId,
      );

      onStatusChange(DeviceFlowStatus.success, null);
      return result;
    } catch (e) {
      // Check if this flow was cancelled (flowId no longer matches current)
      if (flowId != _currentFlowId) {
        onStatusChange(DeviceFlowStatus.cancelled, null);
      } else {
        onStatusChange(DeviceFlowStatus.error, e.toString());
      }
      rethrow;
    }
  }

  /// Cancels any ongoing device flow by incrementing the flow ID.
  void cancelDeviceFlow() {
    _currentFlowId++;
  }

  /// Request a device code from GitHub.
  Future<DeviceCodeInfo> _requestDeviceCode() async {
    final response = await http.post(
      Uri.parse(AppConstants.githubDeviceCodeUrl),
      headers: {
        'Accept': 'application/json',
        'Content-Type': 'application/x-www-form-urlencoded',
      },
      body: {
        'client_id': AppConstants.githubClientId,
        'scope': AppConstants.oauthScopes,
      },
    );

    if (response.statusCode != 200) {
      throw Exception('Failed to request device code: ${response.statusCode}');
    }

    Map<String, dynamic> data;
    try {
      data = json.decode(response.body) as Map<String, dynamic>;
    } catch (e) {
      throw Exception('Invalid response from GitHub');
    }

    if (data.containsKey('error')) {
      throw Exception(data['error_description'] ?? data['error']);
    }

    return DeviceCodeInfo(
      deviceCode: data['device_code'] as String,
      userCode: data['user_code'] as String,
      verificationUri: data['verification_uri'] as String,
      expiresIn: data['expires_in'] as int,
      interval: data['interval'] as int? ?? 5,
    );
  }

  /// Poll GitHub for the access token until user completes authorization.
  Future<OAuthResult> _pollForToken({
    required DeviceCodeInfo deviceCodeInfo,
    required DeviceFlowCallback onStatusChange,
    required int flowId,
  }) async {
    final expiresAt = DateTime.now().add(Duration(seconds: deviceCodeInfo.expiresIn));
    var interval = deviceCodeInfo.interval;

    // Check if this flow is still current (not cancelled or replaced by new flow)
    bool isCurrentFlow() => flowId == _currentFlowId;

    while (isCurrentFlow() && DateTime.now().isBefore(expiresAt)) {
      // Wait for the interval before polling
      await Future.delayed(Duration(seconds: interval));

      // Check again after delay - flow may have been cancelled during wait
      if (!isCurrentFlow()) {
        throw Exception('Device flow cancelled');
      }

      final response = await http.post(
        Uri.parse(AppConstants.githubTokenUrl),
        headers: {
          'Accept': 'application/json',
          'Content-Type': 'application/x-www-form-urlencoded',
        },
        body: {
          'client_id': AppConstants.githubClientId,
          'device_code': deviceCodeInfo.deviceCode,
          'grant_type': 'urn:ietf:params:oauth:grant-type:device_code',
        },
      );

      Map<String, dynamic> data;
      try {
        data = json.decode(response.body) as Map<String, dynamic>;
      } catch (e) {
        throw Exception('Invalid response from GitHub');
      }

      if (data.containsKey('access_token')) {
        // Success!
        final accessToken = data['access_token'] as String;
        final tokenType = data['token_type'] as String? ?? 'bearer';
        final scopeStr = data['scope'] as String? ?? '';
        final scopes = scopeStr.split(',').where((s) => s.isNotEmpty).toList();

        return OAuthResult(
          accessToken: accessToken,
          tokenType: tokenType,
          scopes: scopes,
        );
      }

      if (data.containsKey('error')) {
        final error = data['error'] as String;

        switch (error) {
          case 'authorization_pending':
            // User hasn't completed authorization yet, keep polling
            continue;
          case 'slow_down':
            // We're polling too fast, increase interval
            interval += 5;
            continue;
          case 'expired_token':
            onStatusChange(DeviceFlowStatus.expired, null);
            throw Exception('The device code has expired. Please try again.');
          case 'access_denied':
            throw Exception('Authorization was denied by the user.');
          default:
            throw Exception(data['error_description'] ?? error);
        }
      }
    }

    // Flow exited while loop - check if cancelled or expired
    if (!isCurrentFlow()) {
      throw Exception('Device flow cancelled');
    }

    onStatusChange(DeviceFlowStatus.expired, null);
    throw Exception('The device code has expired. Please try again.');
  }
}
