import 'dart:async';
import 'dart:convert';
import 'package:http/http.dart' as http;

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';

import '../../../core/constants/app_constants.dart';

final tokenRepositoryProvider = Provider<TokenRepository>((ref) {
  return TokenRepository();
});

class TokenRepository {
  final FlutterSecureStorage _storage = const FlutterSecureStorage();

  // In-memory cache to avoid repeated secure storage calls
  String? _cachedToken;
  String? _cachedUsername;
  Completer<void>? _cacheLoadCompleter;

  Future<void> _ensureCacheLoaded() async {
    if (_cacheLoadCompleter?.isCompleted == true) return;

    if (_cacheLoadCompleter != null) {
      await _cacheLoadCompleter!.future;
      return;
    }

    _cacheLoadCompleter = Completer<void>();
    try {
      _cachedToken = await _storage.read(key: AppConstants.tokenStorageKey);
      _cachedUsername = await _storage.read(key: AppConstants.usernameStorageKey);
      _cacheLoadCompleter!.complete();
    } catch (e) {
      _cacheLoadCompleter!.completeError(e);
      _cacheLoadCompleter = null;
      rethrow;
    }
  }

  Future<String?> getToken() async {
    await _ensureCacheLoaded();
    return _cachedToken;
  }

  Future<void> saveToken(String token) async {
    await _storage.write(key: AppConstants.tokenStorageKey, value: token);
    _cachedToken = token;
  }

  Future<void> deleteToken() async {
    await _storage.delete(key: AppConstants.tokenStorageKey);
    _cachedToken = null;
  }

  Future<String?> getUsername() async {
    await _ensureCacheLoaded();
    return _cachedUsername;
  }

  Future<void> saveUsername(String username) async {
    await _storage.write(key: AppConstants.usernameStorageKey, value: username);
    _cachedUsername = username;
  }

  Future<void> deleteUsername() async {
    await _storage.delete(key: AppConstants.usernameStorageKey);
    _cachedUsername = null;
  }

  /// Validates the token by making a request to GitHub API
  /// and checking for necessary scopes.
  /// Returns a verification result string. Null means valid.
  Future<String?> validateToken(String token) async {
    try {
      final response = await http.get(
        Uri.parse('https://api.github.com/user'),
        headers: {
          'Authorization': 'Bearer $token',
          'Accept': 'application/vnd.github+json',
          'X-GitHub-Api-Version': '2022-11-28',
        },
      ).timeout(const Duration(seconds: 10));

      if (response.statusCode == 200) {
        final data = json.decode(response.body) as Map<String, dynamic>;
        final login = data['login'] as String?;
        if (login != null) {
          await saveUsername(login);
        } else {
          return 'Invalid response from GitHub';
        }

        // Check scopes
        final scopesHeader = response.headers['x-oauth-scopes'];
        if (scopesHeader != null) {
          final scopes = scopesHeader.split(',').map((s) => s.trim()).toList();
          if (!scopes.contains('repo') && !scopes.contains('public_repo')) {
            return 'Token is valid but missing "repo" scope. Private repos may not work.';
          }
          // "read:user" is implied for /user access usually?
        }
        
        return null; // Valid
      } else if (response.statusCode == 401) {
        return 'Invalid token';
      } else if (response.statusCode == 403) {
        return 'Rate limit exceeded or forbidden';
      } else {
        return 'Validation failed: ${response.statusCode}';
      }
    } catch (e) {
      return 'Connection error: $e';
    }
  }

  Future<void> clearAll() async {
    await deleteToken();
    await deleteUsername();
    _cacheLoadCompleter = null;
  }
}
