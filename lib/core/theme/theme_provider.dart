import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

final themeModeProvider = StateNotifierProvider<ThemeModeNotifier, ThemeMode>((ref) {
  return ThemeModeNotifier();
});

class ThemeModeNotifier extends StateNotifier<ThemeMode> {
  // Can extend to persist this preference using LocalStorageService later
  ThemeModeNotifier() : super(ThemeMode.dark); 

  void toggle() {
    state = state == ThemeMode.dark ? ThemeMode.light : ThemeMode.dark;
  }
  
  void setMode(ThemeMode mode) {
    state = mode;
  }
}
