import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/services/notification_service.dart';
import '../../../auth/data/organization_repository.dart';
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
    final orgFilter = ref.watch(selectedOrgProvider);
    final prRepo = ref.read(prRepositoryProvider);
    
    // Reset pagination
    _endCursor = null;
    _hasNextPage = true;

    if (query.isNotEmpty) {
      // ------------------------------------------------------------------
      // Search Mode (Server-Side)
      // ------------------------------------------------------------------
      _autoRefreshTimer?.cancel(); // No auto-refresh during search

      // Add org filter to search query if specified
      String searchQuery = query;
      if (orgFilter != null && orgFilter.isNotEmpty) {
        searchQuery = '$query org:$orgFilter';
      }

      final result = await prRepo.searchPullRequests(searchQuery);
      _endCursor = result.endCursor;
      _hasNextPage = result.hasNextPage;

      // Sort by updatedAt descending (most recent first)
      final sorted = List<PullRequestModel>.from(result.items)
        ..sort((a, b) => b.updatedAt.compareTo(a.updatedAt));

      return sorted;
    } else {
      // ------------------------------------------------------------------
      // Default Mode (Review Requests)
      // ------------------------------------------------------------------

      // Try to load from cache first - skip cache if org filter is active
      if (orgFilter == null) {
        try {
          final cached = await prRepo.getCachedReviewRequests();
          if (cached.items.isNotEmpty) {
            _knownPrIds = cached.items.map((pr) => pr.id).toSet();
            _isFirstLoad = false;

            // Trigger network refresh in background
            Future.microtask(() => _refreshNetworkSilent());

            _startAutoRefresh();

            // Sort by updatedAt descending (most recent first)
            final sorted = List<PullRequestModel>.from(cached.items)
              ..sort((a, b) => b.updatedAt.compareTo(a.updatedAt));

            // Debug: Print cache load
            print('=== LOADING FROM CACHE ===');
            for (var i = 0; i < sorted.length && i < 3; i++) {
              print('${i + 1}. ${sorted[i].title}');
              print('   Updated: ${sorted[i].updatedAt}');
              print('   Created: ${sorted[i].createdAt}');
            }
            print('==========================');

            return sorted;
          }
        } catch (e) {
          // Ignore cache errors
        }
      }

      // Fallback to network
      final result = await _fetchPullRequests();
      
      // Update pagination state
      _endCursor = result.endCursor;
      _hasNextPage = result.hasNextPage;
      
      // Start auto-refresh after initial load
      _startAutoRefresh();

      // Sort by updatedAt descending (most recent first)
      final sorted = List<PullRequestModel>.from(result.items)
        ..sort((a, b) => b.updatedAt.compareTo(a.updatedAt));
      
      // Debug: Print first 3 PRs to verify sort order
      print('=== PR Sort Order (Review Requests) ===');
      for (var i = 0; i < sorted.length && i < 3; i++) {
        print('${i + 1}. ${sorted[i].title}');
        print('   Updated: ${sorted[i].updatedAt}');
        print('   Created: ${sorted[i].createdAt}');
      }
      print('=======================================');
      
      return sorted;
    }
  }

  // Helper handling the fetch logic returning PaginatedResult
  Future<dynamic> _fetchPullRequests({String? cursor}) async {
    final prRepo = ref.read(prRepositoryProvider);
    final query = ref.read(prSearchQueryProvider);
    final orgFilter = ref.read(selectedOrgProvider);

    if (query.isNotEmpty) {
      return await prRepo.searchPullRequests(query, afterCursor: cursor);
    } else {
      final result = await prRepo.getReviewRequests(
        afterCursor: cursor,
        orgFilter: orgFilter,
      );
      
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
      
      // Combine and sort all items
      final allItems = <PullRequestModel>[...currentItems, ...result.items];
      allItems.sort((a, b) => b.updatedAt.compareTo(a.updatedAt));
      
      state = AsyncValue.data(allItems);
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
      
      // Sort by updatedAt descending (most recent first)
      final sorted = List<PullRequestModel>.from(result.items)
        ..sort((a, b) => b.updatedAt.compareTo(a.updatedAt));
      
      state = AsyncValue.data(sorted);
    } catch (e) {
      // Silent fail
    }
  }

  Future<void> refresh() async {
    print('=== REFRESH CALLED ===');
    state = const AsyncValue.loading();
    try {
      _endCursor = null;
      final result = await _fetchPullRequests();
      _endCursor = result.endCursor;
      _hasNextPage = result.hasNextPage;
      
      // Sort by updatedAt descending (most recent first)
      final sorted = List<PullRequestModel>.from(result.items)
        ..sort((a, b) => b.updatedAt.compareTo(a.updatedAt));
      
      print('=== REFRESH COMPLETED ===');
      for (var i = 0; i < sorted.length && i < 3; i++) {
        print('${i + 1}. ${sorted[i].title}');
        print('   Updated: ${sorted[i].updatedAt}');
      }
      print('=========================');
      
      state = AsyncValue.data(sorted);
    } catch (e, st) {
      print('=== REFRESH ERROR: $e ===');
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
    final orgFilter = ref.watch(selectedOrgProvider);

    _endCursor = null;
    _hasNextPage = true;

    // Cache logic - skip cache if org filter is active
    if (orgFilter == null) {
      try {
        final cached = await prRepo.getCachedCreatedPrs();
        if (cached.items.isNotEmpty) {
          Future.microtask(() => _refreshNetworkSilent());
          _startAutoRefresh();

          // Sort by updatedAt descending (most recent first)
          final sorted = List<PullRequestModel>.from(cached.items)
            ..sort((a, b) => b.updatedAt.compareTo(a.updatedAt));

          return sorted;
        }
      } catch (e) {
        // ignore
      }
    }

    final result = await prRepo.getCreatedPrs(orgFilter: orgFilter);
    _endCursor = result.endCursor;
    _hasNextPage = result.hasNextPage;

    _startAutoRefresh();

    // Sort by updatedAt descending (most recent first)
    final sorted = List<PullRequestModel>.from(result.items)
      ..sort((a, b) => b.updatedAt.compareTo(a.updatedAt));

    return sorted;
  }

  Future<void> loadMore() async {
    if (!_hasNextPage) return;
    final currentItems = state.value ?? [];
    final prRepo = ref.read(prRepositoryProvider);
    final orgFilter = ref.read(selectedOrgProvider);

    try {
      final result = await prRepo.getCreatedPrs(
        afterCursor: _endCursor,
        orgFilter: orgFilter,
      );
      _endCursor = result.endCursor;
      _hasNextPage = result.hasNextPage;

      // Combine and sort all items
      final allItems = <PullRequestModel>[...currentItems, ...result.items];
      allItems.sort((a, b) => b.updatedAt.compareTo(a.updatedAt));

      state = AsyncValue.data(allItems);
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
      final orgFilter = ref.read(selectedOrgProvider);
      final result = await prRepo.getCreatedPrs(orgFilter: orgFilter);
      _endCursor = result.endCursor;
      _hasNextPage = result.hasNextPage;

      // Sort by updatedAt descending (most recent first)
      final sorted = List<PullRequestModel>.from(result.items)
        ..sort((a, b) => b.updatedAt.compareTo(a.updatedAt));

      state = AsyncValue.data(sorted);
    } catch (e) {
      // silent
    }
  }

  Future<void> refresh() async {
    state = const AsyncValue.loading();
    try {
      final prRepo = ref.read(prRepositoryProvider);
      final orgFilter = ref.read(selectedOrgProvider);
      final result = await prRepo.getCreatedPrs(orgFilter: orgFilter);
      _endCursor = result.endCursor;
      _hasNextPage = result.hasNextPage;

      // Sort by updatedAt descending (most recent first)
      final sorted = List<PullRequestModel>.from(result.items)
        ..sort((a, b) => b.updatedAt.compareTo(a.updatedAt));

      state = AsyncValue.data(sorted);
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
  Timer? _autoRefreshTimer;
  String? _endCursor;
  bool _hasNextPage = true;

  @override
  Future<List<ReviewedPullRequestModel>> build() async {
    ref.onDispose(() {
      _autoRefreshTimer?.cancel();
    });

    final prRepo = ref.read(prRepositoryProvider);
    final orgFilter = ref.watch(selectedOrgProvider);
    _endCursor = null;
    _hasNextPage = true;

    // Cache logic - skip cache if org filter is active
    if (orgFilter == null) {
      try {
        final cached = await prRepo.getCachedReviewedPrs();
        if (cached.items.isNotEmpty) {
          Future.microtask(() => _refreshNetworkSilent());
          _startAutoRefresh();
          return cached.items;
        }
      } catch (e) {
        // ignore
      }
    }

    final result = await prRepo.getReviewedPrs(orgFilter: orgFilter);
    _endCursor = result.endCursor;
    _hasNextPage = result.hasNextPage;
    _startAutoRefresh();
    return result.items;
  }

  Future<void> loadMore() async {
    if (!_hasNextPage) return;
    final currentItems = state.value ?? [];
    final prRepo = ref.read(prRepositoryProvider);
    final orgFilter = ref.read(selectedOrgProvider);

    try {
      final result = await prRepo.getReviewedPrs(
        afterCursor: _endCursor,
        orgFilter: orgFilter,
      );
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
      final orgFilter = ref.read(selectedOrgProvider);
      final result = await prRepo.getReviewedPrs(orgFilter: orgFilter);
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
      final orgFilter = ref.read(selectedOrgProvider);
      final result = await prRepo.getReviewedPrs(orgFilter: orgFilter);
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
  Timer? _autoRefreshTimer;

  @override
  Future<List<CreatedPullRequestModel>> build() async {
    ref.onDispose(() {
      _autoRefreshTimer?.cancel();
    });

    final prRepo = ref.read(prRepositoryProvider);
    try {
      final cached = await prRepo.getCachedRecentlyCreatedPrs();
      if (cached.isNotEmpty) {
        _refreshNetworkSilent();
        _startAutoRefresh();
        return cached;
      }
    } catch (e) {
      // ignore
    }
    final result = await _fetchRecentlyCreatedPrs();
    _startAutoRefresh();
    return result;
  }

  Future<List<CreatedPullRequestModel>> _fetchRecentlyCreatedPrs() async {
    final prRepo = ref.read(prRepositoryProvider);
    return await prRepo.getRecentlyCreatedPrs();
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

// ============================================================================
// Organization Filter Provider
// ============================================================================

final selectedOrgProvider = NotifierProvider<SelectedOrgNotifier, String?>(() {
  return SelectedOrgNotifier();
});

class SelectedOrgNotifier extends Notifier<String?> {
  @override
  String? build() {
    // Load saved selection on startup
    _loadSavedOrg();
    return null;
  }

  Future<void> _loadSavedOrg() async {
    try {
      final orgRepo = ref.read(organizationRepositoryProvider);
      final savedOrg = await orgRepo.getSelectedOrganization();
      if (savedOrg != null) {
        state = savedOrg;
      }
    } catch (e) {
      // Ignore errors loading saved org
    }
  }

  Future<void> setOrg(String? orgLogin) async {
    state = orgLogin;

    // Persist the selection
    try {
      final orgRepo = ref.read(organizationRepositoryProvider);
      await orgRepo.saveSelectedOrganization(orgLogin);
    } catch (e) {
      // Ignore errors saving org
    }
    // PR list providers watch selectedOrgProvider, so they rebuild automatically
  }
}
