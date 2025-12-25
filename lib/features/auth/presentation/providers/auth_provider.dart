import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/services/graphql_service.dart';
import '../../data/organization_repository.dart';
import '../../data/token_repository.dart';

final authProvider = AsyncNotifierProvider<AuthNotifier, bool>(() {
  return AuthNotifier();
});

class AuthNotifier extends AsyncNotifier<bool> {
  @override
  Future<bool> build() async {
    final tokenRepo = ref.read(tokenRepositoryProvider);
    final token = await tokenRepo.getToken();
    return token != null && token.isNotEmpty;
  }

  /// Login using Personal Access Token (PAT).
  Future<String?> login(String token) async {
    state = const AsyncValue.loading();

    final tokenRepo = ref.read(tokenRepositoryProvider);
    final error = await tokenRepo.validateToken(token);

    if (error == null) {
      await tokenRepo.saveToken(token);

      // Fetch organizations after successful login
      await _fetchOrganizations();

      state = const AsyncValue.data(true);
      return null;
    } else {
      state = const AsyncValue.data(false);
      return error;
    }
  }

  /// Login using GitHub OAuth token (from Device Flow).
  Future<String?> loginWithOAuthToken(String token) async {
    state = const AsyncValue.loading();

    try {
      final tokenRepo = ref.read(tokenRepositoryProvider);

      // Validate the token (this also saves the username)
      final error = await tokenRepo.validateToken(token);

      if (error == null) {
        await tokenRepo.saveToken(token);

        // Fetch organizations after successful login
        await _fetchOrganizations();

        state = const AsyncValue.data(true);
        return null;
      } else {
        state = const AsyncValue.data(false);
        return error;
      }
    } catch (e) {
      state = const AsyncValue.data(false);
      return e.toString();
    }
  }

  /// Fetch user organizations after login.
  Future<void> _fetchOrganizations() async {
    try {
      final orgRepo = ref.read(organizationRepositoryProvider);
      await orgRepo.fetchOrganizations();
    } catch (e) {
      // Non-fatal: organizations are optional for the app to work
      // Just log and continue
    }
  }

  Future<void> logout() async {
    final tokenRepo = ref.read(tokenRepositoryProvider);
    final orgRepo = ref.read(organizationRepositoryProvider);
    final graphQLService = ref.read(graphQLServiceProvider);

    await tokenRepo.clearAll();
    await orgRepo.clearAll();
    graphQLService.reset();

    state = const AsyncValue.data(false);
  }
}

// Provider to get current username
final currentUsernameProvider = FutureProvider<String?>((ref) async {
  final tokenRepo = ref.read(tokenRepositoryProvider);
  return await tokenRepo.getUsername();
});

// Provider to get user organizations
final userOrganizationsProvider = FutureProvider<List<GithubOrganization>>((ref) async {
  final orgRepo = ref.read(organizationRepositoryProvider);
  return await orgRepo.getCachedOrganizations();
});

// Provider to refresh organizations
final refreshOrganizationsProvider = FutureProvider<List<GithubOrganization>>((ref) async {
  final orgRepo = ref.read(organizationRepositoryProvider);
  return await orgRepo.fetchOrganizations();
});
