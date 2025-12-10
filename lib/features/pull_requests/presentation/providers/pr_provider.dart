import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/services/notification_service.dart';
import '../../data/pr_repository.dart';
import '../../domain/models/pull_request.dart';

final prListProvider =
    AsyncNotifierProvider<PrListNotifier, List<PullRequestModel>>(() {
  return PrListNotifier();
});

class PrListNotifier extends AsyncNotifier<List<PullRequestModel>> {
  Timer? _autoRefreshTimer;
  Set<int> _knownPrIds = {};
  bool _isFirstLoad = true;

  @override
  Future<List<PullRequestModel>> build() async {
    // Clean up timer when provider is disposed
    ref.onDispose(() {
      _autoRefreshTimer?.cancel();
    });

    final prs = await _fetchPullRequests();

    // Start auto-refresh after initial load
    _startAutoRefresh();

    return prs;
  }

  Future<List<PullRequestModel>> _fetchPullRequests() async {
    final prRepo = ref.read(prRepositoryProvider);
    return await prRepo.getReviewRequests();
  }

  void _startAutoRefresh() {
    _autoRefreshTimer?.cancel();
    _autoRefreshTimer = Timer.periodic(
      const Duration(minutes: 5),
      (_) => _autoRefreshWithNotification(),
    );
  }

  Future<void> _autoRefreshWithNotification() async {
    try {
      final newPrs = await _fetchPullRequests();
      final newPrIds = newPrs.map((pr) => pr.id).toSet();

      // Find truly new PRs (not in our known set)
      if (!_isFirstLoad && _knownPrIds.isNotEmpty) {
        final brandNewPrs = newPrs.where((pr) => !_knownPrIds.contains(pr.id)).toList();

        if (brandNewPrs.isNotEmpty) {
          final notificationService = ref.read(notificationServiceProvider);
          await notificationService.showNewPRNotification(
            count: brandNewPrs.length,
            title: brandNewPrs.first.title,
            repo: brandNewPrs.first.repository.fullName,
          );
        }
      }

      // Update known PR IDs
      _knownPrIds = newPrIds;
      _isFirstLoad = false;

      // Update state without showing loading
      state = AsyncValue.data(newPrs);
    } catch (e) {
      // Silent fail for background refresh - don't update state on error
    }
  }

  Future<void> refresh() async {
    state = const AsyncValue.loading();
    try {
      final prs = await _fetchPullRequests();
      _knownPrIds = prs.map((pr) => pr.id).toSet();
      _isFirstLoad = false;
      state = AsyncValue.data(prs);
    } catch (e, st) {
      state = AsyncValue.error(e, st);
    }
  }

  void stopAutoRefresh() {
    _autoRefreshTimer?.cancel();
    _autoRefreshTimer = null;
  }

  void startAutoRefresh() {
    _startAutoRefresh();
  }
}

// Provider for filtering/searching
final prSearchQueryProvider = StateProvider<String>((ref) => '');

// Filtered PR list based on search query
final filteredPrListProvider = Provider<AsyncValue<List<PullRequestModel>>>((ref) {
  final prList = ref.watch(prListProvider);
  final searchQuery = ref.watch(prSearchQueryProvider).toLowerCase();

  return prList.whenData((prs) {
    if (searchQuery.isEmpty) return prs;

    return prs.where((pr) {
      return pr.title.toLowerCase().contains(searchQuery) ||
          pr.repository.fullName.toLowerCase().contains(searchQuery) ||
          pr.author.login.toLowerCase().contains(searchQuery) ||
          pr.labels.any((l) => l.name.toLowerCase().contains(searchQuery));
    }).toList();
  });
});

// Auto-refresh settings provider
final autoRefreshEnabledProvider = StateProvider<bool>((ref) => true);

// ============================================================================
// Created PRs Provider
// ============================================================================

final createdPrListProvider =
    AsyncNotifierProvider<CreatedPrListNotifier, List<PullRequestModel>>(() {
  return CreatedPrListNotifier();
});

class CreatedPrListNotifier extends AsyncNotifier<List<PullRequestModel>> {
  Timer? _autoRefreshTimer;

  @override
  Future<List<PullRequestModel>> build() async {
    ref.onDispose(() {
      _autoRefreshTimer?.cancel();
    });

    final prs = await _fetchPullRequests();
    _startAutoRefresh();
    return prs;
  }

  Future<List<PullRequestModel>> _fetchPullRequests() async {
    final prRepo = ref.read(prRepositoryProvider);
    return await prRepo.getCreatedPrs();
  }

  void _startAutoRefresh() {
    _autoRefreshTimer?.cancel();
    _autoRefreshTimer = Timer.periodic(
      const Duration(minutes: 5),
      (_) => _autoRefresh(),
    );
  }

  Future<void> _autoRefresh() async {
    try {
      final newPrs = await _fetchPullRequests();
      state = AsyncValue.data(newPrs);
    } catch (e) {
      // Silent fail for background refresh
    }
  }

  Future<void> refresh() async {
    state = const AsyncValue.loading();
    try {
      final prs = await _fetchPullRequests();
      state = AsyncValue.data(prs);
    } catch (e, st) {
      state = AsyncValue.error(e, st);
    }
  }
}

// Filtered Created PR list based on search query
final filteredCreatedPrListProvider =
    Provider<AsyncValue<List<PullRequestModel>>>((ref) {
  final prList = ref.watch(createdPrListProvider);
  final searchQuery = ref.watch(prSearchQueryProvider).toLowerCase();

  return prList.whenData((prs) {
    if (searchQuery.isEmpty) return prs;

    return prs.where((pr) {
      return pr.title.toLowerCase().contains(searchQuery) ||
          pr.repository.fullName.toLowerCase().contains(searchQuery) ||
          pr.author.login.toLowerCase().contains(searchQuery) ||
          pr.labels.any((l) => l.name.toLowerCase().contains(searchQuery));
    }).toList();
  });
});

// ============================================================================
// Reviewed PRs Provider (Last 5 PRs reviewed by user)
// ============================================================================

final reviewedPrListProvider =
    AsyncNotifierProvider<ReviewedPrListNotifier, List<ReviewedPullRequestModel>>(() {
  return ReviewedPrListNotifier();
});

class ReviewedPrListNotifier extends AsyncNotifier<List<ReviewedPullRequestModel>> {
  @override
  Future<List<ReviewedPullRequestModel>> build() async {
    return await _fetchReviewedPrs();
  }

  Future<List<ReviewedPullRequestModel>> _fetchReviewedPrs() async {
    final prRepo = ref.read(prRepositoryProvider);
    return await prRepo.getReviewedPrs();
  }

  Future<void> refresh() async {
    state = const AsyncValue.loading();
    try {
      final prs = await _fetchReviewedPrs();
      state = AsyncValue.data(prs);
    } catch (e, st) {
      state = AsyncValue.error(e, st);
    }
  }
}

// ============================================================================
// Recently Created PRs Provider (Last 5 PRs created by user, any state)
// ============================================================================

final recentlyCreatedPrListProvider =
    AsyncNotifierProvider<RecentlyCreatedPrListNotifier, List<CreatedPullRequestModel>>(() {
  return RecentlyCreatedPrListNotifier();
});

class RecentlyCreatedPrListNotifier extends AsyncNotifier<List<CreatedPullRequestModel>> {
  @override
  Future<List<CreatedPullRequestModel>> build() async {
    return await _fetchRecentlyCreatedPrs();
  }

  Future<List<CreatedPullRequestModel>> _fetchRecentlyCreatedPrs() async {
    final prRepo = ref.read(prRepositoryProvider);
    return await prRepo.getRecentlyCreatedPrs();
  }

  Future<void> refresh() async {
    state = const AsyncValue.loading();
    try {
      final prs = await _fetchRecentlyCreatedPrs();
      state = AsyncValue.data(prs);
    } catch (e, st) {
      state = AsyncValue.error(e, st);
    }
  }
}
