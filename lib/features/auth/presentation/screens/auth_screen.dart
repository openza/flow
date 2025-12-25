import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../../../core/constants/app_constants.dart';
import '../../data/oauth_service.dart';
import '../providers/auth_provider.dart';
import 'token_screen.dart';

class AuthScreen extends ConsumerStatefulWidget {
  const AuthScreen({super.key});

  @override
  ConsumerState<AuthScreen> createState() => _AuthScreenState();
}

class _AuthScreenState extends ConsumerState<AuthScreen> {
  DeviceFlowStatus _status = DeviceFlowStatus.requestingCode;
  String? _userCode;
  String? _errorMessage;
  bool _isLoading = false;

  @override
  void initState() {
    super.initState();
  }

  Future<void> _startDeviceFlow() async {
    setState(() {
      _isLoading = true;
      _errorMessage = null;
      _userCode = null;
      _status = DeviceFlowStatus.requestingCode;
    });

    try {
      final oauthService = ref.read(oauthServiceProvider);
      final result = await oauthService.startDeviceFlow(
        onStatusChange: (status, message) {
          if (mounted) {
            setState(() {
              _status = status;
              if (status == DeviceFlowStatus.waitingForUser ||
                  status == DeviceFlowStatus.polling) {
                _userCode = message;
              }
            });
          }
        },
      );

      // Validate and save token
      final error = await ref.read(authProvider.notifier).loginWithOAuthToken(
            result.accessToken,
          );

      if (error != null && mounted) {
        setState(() {
          _isLoading = false;
          _errorMessage = error;
          _status = DeviceFlowStatus.error;
        });
      }
    } catch (e) {
      if (mounted) {
        setState(() {
          _isLoading = false;
          _errorMessage = e.toString().replaceAll('Exception: ', '');
          _status = DeviceFlowStatus.error;
        });
      }
    }
  }

  void _cancelDeviceFlow() {
    ref.read(oauthServiceProvider).cancelDeviceFlow();
    setState(() {
      _isLoading = false;
      _userCode = null;
      _status = DeviceFlowStatus.cancelled;
    });
  }

  void _copyCode() {
    if (_userCode != null) {
      Clipboard.setData(ClipboardData(text: _userCode!));
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('Code copied to clipboard'),
          duration: Duration(seconds: 2),
        ),
      );
    }
  }

  Future<void> _openGitHubDevice() async {
    final url = Uri.parse(AppConstants.githubDeviceVerificationUrl);
    if (await canLaunchUrl(url)) {
      await launchUrl(url, mode: LaunchMode.externalApplication);
    }
  }

  void _navigateToTokenScreen() {
    Navigator.of(context).push(
      MaterialPageRoute(builder: (_) => const TokenScreen()),
    );
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;

    return Scaffold(
      body: Center(
        child: SingleChildScrollView(
          padding: const EdgeInsets.all(AppConstants.largePadding),
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 400),
            child: Column(
              mainAxisAlignment: MainAxisAlignment.center,
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                // App icon/logo
                Icon(
                  Icons.inbox_rounded,
                  size: 64,
                  color: colorScheme.primary,
                ),
                const SizedBox(height: AppConstants.defaultPadding),

                // Title
                Text(
                  AppConstants.appName,
                  style: theme.textTheme.headlineLarge,
                  textAlign: TextAlign.center,
                ),
                const SizedBox(height: AppConstants.smallPadding),

                // Subtitle
                Text(
                  'GitHub PR Review Inbox',
                  style: theme.textTheme.bodyMedium,
                  textAlign: TextAlign.center,
                ),
                const SizedBox(height: AppConstants.largePadding * 2),

                // Show different UI based on status
                if (_userCode != null &&
                    (_status == DeviceFlowStatus.waitingForUser ||
                        _status == DeviceFlowStatus.polling))
                  _buildDeviceCodeUI(theme, colorScheme)
                else
                  _buildInitialUI(theme, colorScheme),
              ],
            ),
          ),
        ),
      ),
    );
  }

  Widget _buildInitialUI(ThemeData theme, ColorScheme colorScheme) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        // OAuth Login Button
        SizedBox(
          height: 52,
          child: ElevatedButton.icon(
            onPressed: _isLoading ? null : _startDeviceFlow,
            icon: _isLoading
                ? const SizedBox(
                    width: 20,
                    height: 20,
                    child: CircularProgressIndicator(
                      strokeWidth: 2,
                      color: Colors.white,
                    ),
                  )
                : const Icon(Icons.login_rounded),
            label: Text(
              _isLoading ? 'Connecting...' : 'Sign in with GitHub',
              style: const TextStyle(fontSize: 16),
            ),
            style: ElevatedButton.styleFrom(
              backgroundColor: const Color(0xFF24292F),
              foregroundColor: Colors.white,
            ),
          ),
        ),

        if (_errorMessage != null) ...[
          const SizedBox(height: AppConstants.defaultPadding),
          Container(
            padding: const EdgeInsets.all(AppConstants.smallPadding),
            decoration: BoxDecoration(
              color: colorScheme.errorContainer,
              borderRadius: BorderRadius.circular(8),
            ),
            child: Row(
              children: [
                Icon(
                  Icons.error_outline_rounded,
                  color: colorScheme.error,
                  size: 20,
                ),
                const SizedBox(width: 8),
                Expanded(
                  child: Text(
                    _errorMessage!,
                    style: TextStyle(
                      color: colorScheme.error,
                      fontSize: 13,
                    ),
                  ),
                ),
              ],
            ),
          ),
        ],

        const SizedBox(height: AppConstants.largePadding * 2),

        // Divider with "OR"
        Row(
          children: [
            const Expanded(child: Divider()),
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 16),
              child: Text(
                'OR',
                style: theme.textTheme.bodySmall?.copyWith(
                  color: theme.textTheme.bodySmall?.color?.withValues(alpha: 0.6),
                ),
              ),
            ),
            const Expanded(child: Divider()),
          ],
        ),

        const SizedBox(height: AppConstants.largePadding),

        // PAT fallback option
        Container(
          padding: const EdgeInsets.all(AppConstants.defaultPadding),
          decoration: BoxDecoration(
            color: colorScheme.surface,
            borderRadius: BorderRadius.circular(8),
            border: Border.all(color: colorScheme.outline.withValues(alpha: 0.5)),
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Icon(
                    Icons.key_rounded,
                    size: 18,
                    color: theme.textTheme.bodyMedium?.color,
                  ),
                  const SizedBox(width: 8),
                  Text(
                    'Use Personal Access Token',
                    style: theme.textTheme.titleSmall,
                  ),
                ],
              ),
              const SizedBox(height: AppConstants.smallPadding),
              Text(
                'For advanced users or restricted environments.',
                style: theme.textTheme.bodySmall?.copyWith(
                  color: theme.textTheme.bodySmall?.color?.withValues(alpha: 0.7),
                ),
              ),
              const SizedBox(height: AppConstants.smallPadding),
              TextButton(
                onPressed: _navigateToTokenScreen,
                style: TextButton.styleFrom(
                  padding: EdgeInsets.zero,
                  minimumSize: Size.zero,
                  tapTargetSize: MaterialTapTargetSize.shrinkWrap,
                ),
                child: const Text('Enter token manually →'),
              ),
            ],
          ),
        ),

        const SizedBox(height: AppConstants.largePadding * 2),

        // Footer info
        Text(
          'GitDesk needs access to your GitHub account to display your pull requests.',
          style: theme.textTheme.bodySmall?.copyWith(
            color: theme.textTheme.bodySmall?.color?.withValues(alpha: 0.6),
          ),
          textAlign: TextAlign.center,
        ),
      ],
    );
  }

  Widget _buildDeviceCodeUI(ThemeData theme, ColorScheme colorScheme) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        // Instructions
        Text(
          'Enter this code on GitHub',
          style: theme.textTheme.titleMedium,
          textAlign: TextAlign.center,
        ),
        const SizedBox(height: AppConstants.defaultPadding),

        // User code display
        Container(
          padding: const EdgeInsets.symmetric(
            horizontal: AppConstants.largePadding,
            vertical: AppConstants.defaultPadding,
          ),
          decoration: BoxDecoration(
            color: colorScheme.surfaceContainerHighest,
            borderRadius: BorderRadius.circular(12),
            border: Border.all(color: colorScheme.outline),
          ),
          child: Column(
            children: [
              SelectableText(
                _userCode!,
                style: theme.textTheme.headlineLarge?.copyWith(
                  fontFamily: 'monospace',
                  fontWeight: FontWeight.bold,
                  letterSpacing: 4,
                  color: colorScheme.primary,
                ),
                textAlign: TextAlign.center,
              ),
              const SizedBox(height: AppConstants.smallPadding),
              TextButton.icon(
                onPressed: _copyCode,
                icon: const Icon(Icons.copy_rounded, size: 16),
                label: const Text('Copy code'),
                style: TextButton.styleFrom(
                  padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 4),
                  minimumSize: Size.zero,
                  tapTargetSize: MaterialTapTargetSize.shrinkWrap,
                ),
              ),
            ],
          ),
        ),

        const SizedBox(height: AppConstants.defaultPadding),

        // Status indicator
        Row(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            SizedBox(
              width: 16,
              height: 16,
              child: CircularProgressIndicator(
                strokeWidth: 2,
                color: colorScheme.primary,
              ),
            ),
            const SizedBox(width: 8),
            Text(
              'Waiting for authorization...',
              style: theme.textTheme.bodyMedium?.copyWith(
                color: theme.textTheme.bodyMedium?.color?.withValues(alpha: 0.7),
              ),
            ),
          ],
        ),

        const SizedBox(height: AppConstants.largePadding),

        // Open GitHub button
        OutlinedButton.icon(
          onPressed: _openGitHubDevice,
          icon: const Icon(Icons.open_in_new_rounded),
          label: const Text('Open github.com/login/device'),
        ),

        const SizedBox(height: AppConstants.smallPadding),

        // Cancel button
        TextButton(
          onPressed: _cancelDeviceFlow,
          child: const Text('Cancel'),
        ),

        const SizedBox(height: AppConstants.largePadding),

        // Help text
        Container(
          padding: const EdgeInsets.all(AppConstants.defaultPadding),
          decoration: BoxDecoration(
            color: colorScheme.surface,
            borderRadius: BorderRadius.circular(8),
            border: Border.all(color: colorScheme.outline.withValues(alpha: 0.5)),
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Icon(
                    Icons.info_outline_rounded,
                    size: 16,
                    color: theme.textTheme.bodyMedium?.color,
                  ),
                  const SizedBox(width: 8),
                  Text(
                    'How it works',
                    style: theme.textTheme.titleSmall,
                  ),
                ],
              ),
              const SizedBox(height: AppConstants.smallPadding),
              Text(
                '1. Copy the code above\n'
                '2. Open github.com/login/device\n'
                '3. Paste the code and authorize',
                style: theme.textTheme.bodySmall,
              ),
            ],
          ),
        ),
      ],
    );
  }
}
