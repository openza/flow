import 'dart:convert';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:hive_ce_flutter/hive_flutter.dart';
import 'package:path_provider/path_provider.dart';

// Service provider
final localStorageServiceProvider = Provider<LocalStorageService>((ref) {
  return LocalStorageService();
});

class LocalStorageService {
  static const String _boxName = 'gitdesk_cache';
  static const String _prCacheKey = 'pr_cache';
  
  bool _initialized = false;
  late Box _box;

  Future<void> initialize() async {
    if (_initialized) return;

    await Hive.initFlutter();
    
    // Open the box
    _box = await Hive.openBox(_boxName);
    
    _initialized = true;
  }

  Future<void> cachePrData(String key, Map<String, dynamic> data) async {
    if (!_initialized) await initialize();
    await _box.put(key, json.encode(data));
  }

  Future<Map<String, dynamic>?> getCachedPrData(String key) async {
    if (!_initialized) await initialize();
    
    final String? jsonString = _box.get(key);
    if (jsonString == null) return null;
    
    try {
      return json.decode(jsonString) as Map<String, dynamic>;
    } catch (e) {
      return null;
    }
  }

  Future<void> clearCache() async {
    if (!_initialized) await initialize();
    await _box.deleteFromDisk();
    _box = await Hive.openBox(_boxName); // Re-open and reassign reference
  }
}
