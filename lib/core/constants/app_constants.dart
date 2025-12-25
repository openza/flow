class AppConstants {
  AppConstants._();

  static const String appName = 'GitDesk';
  static const String appVersion = '0.2.0';

  // Storage keys
  static const String tokenStorageKey = 'github_pat';
  static const String usernameStorageKey = 'github_username';
  static const String selectedOrgStorageKey = 'selected_org';
  static const String userOrgsStorageKey = 'user_orgs';
  static const String authMethodStorageKey = 'auth_method';

  // GitHub API
  static const String githubApiBaseUrl = 'https://api.github.com';

  // GitHub OAuth (Device Flow - no client secret needed)
  static const String githubClientId = 'Ov23li3xrNtNs8USgNb0';
  static const String githubDeviceCodeUrl = 'https://github.com/login/device/code';
  static const String githubTokenUrl = 'https://github.com/login/oauth/access_token';
  static const String githubDeviceVerificationUrl = 'https://github.com/login/device';
  static const String oauthScopes = 'repo read:user read:org';

  // UI
  static const double defaultPadding = 16.0;
  static const double smallPadding = 8.0;
  static const double largePadding = 24.0;
  static const double cardBorderRadius = 8.0;
  static const double avatarSize = 32.0;
  static const double smallAvatarSize = 20.0;

  // Animation durations
  static const Duration shortAnimation = Duration(milliseconds: 150);
  static const Duration mediumAnimation = Duration(milliseconds: 300);
  static const Duration longAnimation = Duration(milliseconds: 500);
}
