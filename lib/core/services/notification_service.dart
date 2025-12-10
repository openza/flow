import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:local_notifier/local_notifier.dart';

import '../constants/app_constants.dart';

final notificationServiceProvider = Provider<NotificationService>((ref) {
  return NotificationService();
});

class NotificationService {
  bool _initialized = false;

  Future<void> initialize() async {
    if (_initialized) return;

    await localNotifier.setup(
      appName: AppConstants.appName,
      shortcutPolicy: ShortcutPolicy.requireCreate,
    );
    _initialized = true;
  }

  Future<void> showNewPRNotification({
    required int count,
    required String title,
    required String repo,
  }) async {
    if (!_initialized) await initialize();

    final notification = LocalNotification(
      identifier: 'new_pr_${DateTime.now().millisecondsSinceEpoch}',
      title: count == 1
          ? 'New PR Review Request'
          : '$count New PR Review Requests',
      body: count == 1 ? '$title\n$repo' : 'You have $count new PRs to review',
    );

    notification.onClick = () {
      // Notification clicked - app will be focused
    };

    await notification.show();
  }

  Future<void> showNotification({
    required String title,
    required String body,
    String? identifier,
  }) async {
    if (!_initialized) await initialize();

    final notification = LocalNotification(
      identifier: identifier ?? 'gitdesk_${DateTime.now().millisecondsSinceEpoch}',
      title: title,
      body: body,
    );

    await notification.show();
  }
}
