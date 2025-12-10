import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/services/notification_service.dart';
import '../../data/pr_repository.dart';
import '../../domain/models/pull_request.dart';

// Search Query Provider (UI updates this)
final prSearchQueryProvider = StateProvider<String>((ref) => '');

// Main List Provider
final prListProvider =
    AsyncNotifierProvider<PrListNotifier, List<PullRequestModel>>(() {
  return PrListNotifier();
});

class PrListNotifier extends AsyncNotifier<List<PullRequestModel>> {
  Timer? _autoRefreshTimer;
  Set<int> _knownPrIds = {};
  bool _isFirstLoad = true;
  
  // Pagination state
  String? _endCursor;
  bool _hasNextPage = true;

  @override
  Future<List<PullRequestModel>> build() async {
    ref.onDispose(() {
      _autoRefreshTimer?.cancel();
    });

    final query = ref.watch(prSearchQueryProvider);
    final prRepo = ref.read(prRepositoryProvider);
    
    // Reset pagination
    _endCursor = null;
    _hasNextPage = true;

    if (query.isNotEmpty) {
      // ------------------------------------------------------------------
      // Search Mode (Server-Side)
      // ------------------------------------------------------------------
      _autoRefreshTimer?.cancel(); // No auto-refresh during search
      
      final result = await prRepo.searchPullRequests(query);
      _endCursor = result.endCursor;
      _hasNextPage = result.hasNextPage;
      return result.items;
    } else {
      // ------------------------------------------------------------------
      // Default Mode (Review Requests)
      // ------------------------------------------------------------------
      
      // Try to load from cache first
      try {
        final cached = await prRepo.getCachedReviewRequests();
        if (cached.items.isNotEmpty) {
          _knownPrIds = cached.items.map((pr) => pr.id).toSet();
          _isFirstLoad = false;
          
          // Trigger network refresh in background
          Future.microtask(() => _refreshNetworkSilent());
          
          _startAutoRefresh();
          return cached.items;
        }
      } catch (e) {
        // Ignore cache errors
      }

      // Fallback to network
      final result = await _fetchPullRequests();
      
      // Update pagination state
      _endCursor = result.endCursor;
      _hasNextPage = result.hasNextPage;
      
      // Start auto-refresh after initial load
      _startAutoRefresh();

      return result.items;
    }
  }

  // Helper handling the fetch logic returning PaginatedResult
  Future<dynamic> _fetchPullRequests({String? cursor}) async {
    final prRepo = ref.read(prRepositoryProvider);
    final query = ref.read(prSearchQueryProvider);
    
    if (query.isNotEmpty) {
      return await prRepo.searchPullRequests(query, afterCursor: cursor);
    } else {
      final result = await prRepo.getReviewRequests(afterCursor: cursor);
      
      if (cursor == null) {
        // Only update notifications/known IDs on initial page load (refresh)
        final prs = result.items;
        final newPrIds = prs.map((pr) => pr.id).toSet();
        
        if (!_isFirstLoad && _knownPrIds.isNotEmpty) {
            final brandNewPrs = prs.where((pr) => !_knownPrIds.contains(pr.id)).toList();

            if (brandNewPrs.isNotEmpty) {
              final notificationService = ref.read(notificationServiceProvider);
              await notificationService.showNewPRNotification(
                count: brandNewPrs.length,
                title: brandNewPrs.first.title,
                repo: brandNewPrs.first.repository.fullName,
              );
            }
        }
        
        _knownPrIds = newPrIds;
        _isFirstLoad = false;
      }
      return result;
    }
  }

  Future<void> loadMore() async {
    if (!_hasNextPage || state.isLoading) return;
    
    final currentItems = state.value ?? [];
    
    try {
      final result = await _fetchPullRequests(cursor: _endCursor);
      
      _endCursor = result.endCursor;
      _hasNextPage = result.hasNextPage;
      
      state = AsyncValue.data([...currentItems, ...result.items]);
    } catch (e) {
      // Handle error
    }
  }

  void _startAutoRefresh() {
    _autoRefreshTimer?.cancel();
    _autoRefreshTimer = Timer.periodic(
      const Duration(minutes: 5),
      (_) => _refreshNetworkSilent(),
    );
  }
  
  Future<void> _refreshNetworkSilent() async {
    try {
      final result = await _fetchPullRequests();
      _endCursor = result.endCursor;
      _hasNextPage = result.hasNextPage;
      state = AsyncValue.data(result.items);
    } catch (e) {
      // Silent fail
    }
  }

  Future<void> refresh() async {
    state = const AsyncValue.loading();
    try {
      _endCursor = null;
      final result = await _fetchPullRequests();
      _endCursor = result.endCursor;
      _hasNextPage = result.hasNextPage;
      state = AsyncValue.data(result.items);
    } catch (e, st) {
      state = AsyncValue.error(e, st);
    }
  }
}

// Filtered PR list - now just strictly passes through the server-side results
// We keep the provider name to avoid breaking UI imports
final filteredPrListProvider = Provider<AsyncValue<List<PullRequestModel>>>((ref) {
  return ref.watch(prListProvider);
});

// ============================================================================
// Created PRs Provider
// ============================================================================

final createdPrListProvider =
    AsyncNotifierProvider<CreatedPrListNotifier, List<PullRequestModel>>(() {
  return CreatedPrListNotifier();
});

class CreatedPrListNotifier extends AsyncNotifier<List<PullRequestModel>> {
  Timer? _autoRefreshTimer;
  String? _endCursor;
  bool _hasNextPage = true;

  @override
  Future<List<PullRequestModel>> build() async {
    ref.onDispose(() {
      _autoRefreshTimer?.cancel();
    });

    final prRepo = ref.read(prRepositoryProvider);

    _endCursor = null;
    _hasNextPage = true;

    // Cache logic
    try {
      final cached = await prRepo.getCachedCreatedPrs();
      if (cached.items.isNotEmpty) {
        Future.microtask(() => _refreshNetworkSilent());
        _startAutoRefresh();
        return cached.items;
      }
    } catch (e) {
      // ignore
    }

    final result = await prRepo.getCreatedPrs();
    _endCursor = result.endCursor;
    _hasNextPage = result.hasNextPage;
    
    _startAutoRefresh();
    return result.items;
  }

  Future<void> loadMore() async {
    if (!_hasNextPage) return;
    final currentItems = state.value ?? [];
    final prRepo = ref.read(prRepositoryProvider);
    
    try {
      final result = await prRepo.getCreatedPrs(afterCursor: _endCursor);
      _endCursor = result.endCursor;
      _hasNextPage = result.hasNextPage;
      state = AsyncValue.data([...currentItems, ...result.items]);
    } catch (e) {
      // ignore
    }
  }

  void _startAutoRefresh() {
    _autoRefreshTimer?.cancel();
    _autoRefreshTimer = Timer.periodic(
      const Duration(minutes: 5),
      (_) => _refreshNetworkSilent(),
    );
  }

  Future<void> _refreshNetworkSilent() async {
    try {
      final prRepo = ref.read(prRepositoryProvider);
      final result = await prRepo.getCreatedPrs(); // First page
      _endCursor = result.endCursor;
      _hasNextPage = result.hasNextPage;
      state = AsyncValue.data(result.items);
    } catch (e) {
      // silent
    }
  }

  Future<void> refresh() async {
    state = const AsyncValue.loading();
    try {
      final prRepo = ref.read(prRepositoryProvider);
      final result = await prRepo.getCreatedPrs();
      _endCursor = result.endCursor;
      _hasNextPage = result.hasNextPage;
      state = AsyncValue.data(result.items);
    } catch (e, st) {
      state = AsyncValue.error(e, st);
    }
  }
}

// Filtered Created PR list - pass through
final filteredCreatedPrListProvider = Provider<AsyncValue<List<PullRequestModel>>>((ref) {
  return ref.watch(createdPrListProvider);
});

// ============================================================================
// Reviewed PRs Provider (Last 5 PRs reviewed by user) - Pagination supported now
// ============================================================================

final reviewedPrListProvider =
    AsyncNotifierProvider<ReviewedPrListNotifier, List<ReviewedPullRequestModel>>(() {
  return ReviewedPrListNotifier();
});

class ReviewedPrListNotifier extends AsyncNotifier<List<ReviewedPullRequestModel>> {
  String? _endCursor;
  bool _hasNextPage = true;

  @override
  Future<List<ReviewedPullRequestModel>> build() async {
    final prRepo = ref.read(prRepositoryProvider);
    _endCursor = null;
    _hasNextPage = true;
    
    try {
      final cached = await prRepo.getCachedReviewedPrs();
      if (cached.items.isNotEmpty) {
        Future.microtask(() => _refreshNetworkSilent());
        return cached.items;
      }
    } catch (e) {
      // ignore
    }
    
    final result = await prRepo.getReviewedPrs();
    _endCursor = result.endCursor;
    _hasNextPage = result.hasNextPage;
    return result.items;
  }
  
  Future<void> loadMore() async {
    if (!_hasNextPage) return;
    final currentItems = state.value ?? [];
    final prRepo = ref.read(prRepositoryProvider);
    
    try {
      final result = await prRepo.getReviewedPrs(afterCursor: _endCursor);
      _endCursor = result.endCursor;
      _hasNextPage = result.hasNextPage;
      state = AsyncValue.data([...currentItems, ...result.items]);
    } catch (e) {
      // ignore
    }
  }

  Future<void> _refreshNetworkSilent() async {
     try {
       final prRepo = ref.read(prRepositoryProvider);
       final result = await prRepo.getReviewedPrs();
       _endCursor = result.endCursor;
       _hasNextPage = result.hasNextPage;
       state = AsyncValue.data(result.items);
     } catch (e) {
       // ignore
     }
  }

  Future<void> refresh() async {
    state = const AsyncValue.loading();
    try {
      final prRepo = ref.read(prRepositoryProvider);
      final result = await prRepo.getReviewedPrs();
      _endCursor = result.endCursor;
      _hasNextPage = result.hasNextPage;
      state = AsyncValue.data(result.items);
    } catch (e, st) {
      state = AsyncValue.error(e, st);
    }
  }
}

// Recently Created - No changes needed to logic (it returns List), but provider needs to match interface?
// PrRepository returns List<CreatedPullRequestModel> for getRecentlyCreatedPrs (no PaginatedResult).
// So we keep it as is.
final recentlyCreatedPrListProvider =
    AsyncNotifierProvider<RecentlyCreatedPrListNotifier, List<CreatedPullRequestModel>>(() {
  return RecentlyCreatedPrListNotifier();
});

class RecentlyCreatedPrListNotifier extends AsyncNotifier<List<CreatedPullRequestModel>> {
  @override
  Future<List<CreatedPullRequestModel>> build() async {
    final prRepo = ref.read(prRepositoryProvider);
    try {
      final cached = await prRepo.getCachedRecentlyCreatedPrs();
      if (cached.isNotEmpty) {
        _refreshNetworkSilent();
        return cached;
      }
    } catch (e) {
      // ignore
    }
    return await _fetchRecentlyCreatedPrs();
  }

  Future<List<CreatedPullRequestModel>> _fetchRecentlyCreatedPrs() async {
    final prRepo = ref.read(prRepositoryProvider);
    return await prRepo.getRecentlyCreatedPrs();
  }
  
  Future<void> _refreshNetworkSilent() async {
     try {
       final prs = await _fetchRecentlyCreatedPrs();
       state = AsyncValue.data(prs);
     } catch (e) {
       // ignore
     }
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
