import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:github/github.dart';

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
    // If already loaded, return immediately
    if (_cacheLoadCompleter?.isCompleted == true) return;

    // If loading is in progress, wait for it
    if (_cacheLoadCompleter != null) {
      await _cacheLoadCompleter!.future;
      return;
    }

    // Start loading
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

  Future<bool> validateToken(String token) async {
    try {
      final github = GitHub(auth: Authentication.withToken(token));
      final user = await github.users.getCurrentUser();
      if (user.login != null) {
        await saveUsername(user.login!);
        return true;
      }
      return false;
    } catch (e) {
      return false;
    }
  }

  Future<void> clearAll() async {
    await deleteToken();
    await deleteUsername();
    _cacheLoadCompleter = null;
  }
}
