import 'package:flutter/material.dart';

class AppTheme {
  AppTheme._();

  // Dark Colors
  static const Color _darkPrimary = Color(0xFF58A6FF);
  static const Color _darkBackground = Color(0xFF0D1117);
  static const Color _darkSurface = Color(0xFF161B22);
  static const Color _darkCard = Color(0xFF21262D);
  static const Color _darkBorder = Color(0xFF30363D);
  static const Color _darkTextPrimary = Color(0xFFE6EDF3);
  static const Color _darkTextSecondary = Color(0xFF8B949E);
  static const Color _darkTextMuted = Color(0xFF6E7681);
  static const Color _darkSuccess = Color(0xFF3FB950);
  static const Color _darkWarning = Color(0xFFD29922);
  static const Color _darkError = Color(0xFFF85149);

  // Light Colors
  static const Color _lightPrimary = Color(0xFF0969DA);
  static const Color _lightBackground = Color(0xFFFFFFFF);
  static const Color _lightSurface = Color(0xFFF6F8FA);
  static const Color _lightCard = Color(0xFFFFFFFF); // GitHub cards are often just bordered on white or subtle
  static const Color _lightBorder = Color(0xFFD0D7DE);
  static const Color _lightTextPrimary = Color(0xFF1F2328); // fg.default
  static const Color _lightTextSecondary = Color(0xFF656D76); // fg.muted
  static const Color _lightTextMuted = Color(0xFF6E7781);
  static const Color _lightSuccess = Color(0xFF1A7F37);
  static const Color _lightWarning = Color(0xFF9A6700);
  static const Color _lightError = Color(0xFFCF222E);

  static ThemeData get darkTheme => _buildTheme(
        brightness: Brightness.dark,
        primary: _darkPrimary,
        background: _darkBackground,
        surface: _darkSurface,
        card: _darkCard,
        border: _darkBorder,
        textPrimary: _darkTextPrimary,
        textSecondary: _darkTextSecondary,
        textMuted: _darkTextMuted,
        success: _darkSuccess,
        warning: _darkWarning,
        error: _darkError,
      );

  static ThemeData get lightTheme => _buildTheme(
        brightness: Brightness.light,
        primary: _lightPrimary,
        background: _lightBackground,
        surface: _lightSurface,
        card: _lightCard,
        border: _lightBorder,
        textPrimary: _lightTextPrimary,
        textSecondary: _lightTextSecondary,
        textMuted: _lightTextMuted,
        success: _lightSuccess,
        warning: _lightWarning,
        error: _lightError,
      );

  static ThemeData _buildTheme({
    required Brightness brightness,
    required Color primary,
    required Color background,
    required Color surface,
    required Color card,
    required Color border,
    required Color textPrimary,
    required Color textSecondary,
    required Color textMuted,
    required Color success,
    required Color warning,
    required Color error,
  }) {
    return ThemeData(
      useMaterial3: true,
      brightness: brightness,
      primaryColor: primary,
      scaffoldBackgroundColor: background,
      colorScheme: ColorScheme(
        brightness: brightness,
        primary: primary,
        onPrimary: Colors.white,
        secondary: success,
        onSecondary: Colors.white,
        tertiary: warning,
        onTertiary: Colors.white,
        error: error,
        onError: Colors.white,
        surface: surface,
        onSurface: textPrimary,
        outline: border,
      ),
      cardTheme: CardThemeData(
        color: card,
        elevation: 0,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(8),
          side: BorderSide(color: border, width: 1),
        ),
      ),
      appBarTheme: AppBarTheme(
        backgroundColor: surface,
        foregroundColor: textPrimary,
        elevation: 0,
        centerTitle: false,
        titleTextStyle: TextStyle(
          color: textPrimary,
          fontSize: 16,
          fontWeight: FontWeight.w600,
        ),
        iconTheme: IconThemeData(color: textSecondary),
      ),
      textTheme: TextTheme(
        headlineLarge: TextStyle(
          color: textPrimary,
          fontSize: 24,
          fontWeight: FontWeight.bold,
        ),
        headlineMedium: TextStyle(
          color: textPrimary,
          fontSize: 20,
          fontWeight: FontWeight.w600,
        ),
        titleLarge: TextStyle(
          color: textPrimary,
          fontSize: 16,
          fontWeight: FontWeight.w600,
        ),
        titleMedium: TextStyle(
          color: textPrimary,
          fontSize: 14,
          fontWeight: FontWeight.w500,
        ),
        bodyLarge: TextStyle(
          color: textPrimary,
          fontSize: 14,
        ),
        bodyMedium: TextStyle(
          color: textSecondary,
          fontSize: 13,
        ),
        bodySmall: TextStyle(
          color: textMuted,
          fontSize: 12,
        ),
        labelMedium: TextStyle(
          color: textSecondary,
          fontSize: 12,
          fontWeight: FontWeight.w500,
        ),
      ),
      inputDecorationTheme: InputDecorationTheme(
        filled: true,
        fillColor: brightness == Brightness.dark ? const Color(0xFF0D1117) : Colors.white, // In light mode inputs are often white on gray surface
        contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 14),
        border: OutlineInputBorder(
          borderRadius: BorderRadius.circular(8),
          borderSide: BorderSide(color: border),
        ),
        enabledBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(8),
          borderSide: BorderSide(color: border),
        ),
        focusedBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(8),
          borderSide: BorderSide(color: primary, width: 2),
        ),
        errorBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(8),
          borderSide: BorderSide(color: error),
        ),
        hintStyle: TextStyle(color: textMuted),
        labelStyle: TextStyle(color: textSecondary),
      ),
      elevatedButtonTheme: ElevatedButtonThemeData(
        style: ElevatedButton.styleFrom(
          backgroundColor: primary,
          foregroundColor: Colors.white,
          padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 14),
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(8),
          ),
          textStyle: const TextStyle(
            fontSize: 14,
            fontWeight: FontWeight.w600,
          ),
        ),
      ),
      outlinedButtonTheme: OutlinedButtonThemeData(
        style: OutlinedButton.styleFrom(
          foregroundColor: textPrimary,
          padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 14),
          side: BorderSide(color: border),
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(8),
          ),
        ),
      ),
      textButtonTheme: TextButtonThemeData(
        style: TextButton.styleFrom(
          foregroundColor: primary,
        ),
      ),
      iconTheme: IconThemeData(
        color: textSecondary,
        size: 20,
      ),
      dividerTheme: DividerThemeData(
        color: border,
        thickness: 1,
      ),
      chipTheme: ChipThemeData(
        backgroundColor: surface,
        labelStyle: TextStyle(
          color: textPrimary,
          fontSize: 12,
          fontWeight: FontWeight.w500,
        ),
        side: BorderSide(color: border),
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(16),
        ),
        padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
      ),
      snackBarTheme: SnackBarThemeData(
        backgroundColor: card,
        contentTextStyle: TextStyle(color: textPrimary),
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(8),
        ),
        behavior: SnackBarBehavior.floating,
      ),
    );
  }
}
