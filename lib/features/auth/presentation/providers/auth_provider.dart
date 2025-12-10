import 'package:flutter_riverpod/flutter_riverpod.dart';

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

  Future<String?> login(String token) async {
    state = const AsyncValue.loading();

    final tokenRepo = ref.read(tokenRepositoryProvider);
    final error = await tokenRepo.validateToken(token);

    if (error == null) {
      await tokenRepo.saveToken(token);
      state = const AsyncValue.data(true);
      return null;
    } else {
      state = const AsyncValue.data(false);
      return error;
    }
  }

  Future<void> logout() async {
    final tokenRepo = ref.read(tokenRepositoryProvider);
    await tokenRepo.clearAll();
    state = const AsyncValue.data(false);
  }
}

// Provider to get current username
final currentUsernameProvider = FutureProvider<String?>((ref) async {
  final tokenRepo = ref.read(tokenRepositoryProvider);
  return await tokenRepo.getUsername();
});
