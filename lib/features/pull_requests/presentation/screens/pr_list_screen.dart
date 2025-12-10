import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/constants/app_constants.dart';
import '../../../../core/theme/theme_provider.dart';
import '../../../auth/presentation/providers/auth_provider.dart';
import '../../domain/models/pull_request.dart';
import '../providers/pr_provider.dart';
import '../widgets/paginated_pr_list.dart';
import '../widgets/pr_card.dart';

class PrListScreen extends ConsumerStatefulWidget {
  const PrListScreen({super.key});

  @override
  ConsumerState<PrListScreen> createState() => _PrListScreenState();
}

class _PrListScreenState extends ConsumerState<PrListScreen>
    with SingleTickerProviderStateMixin {
  final _searchController = TextEditingController();
  late TabController _tabController;
  bool _isRefreshing = false;
  Timer? _debounceTimer;

  @override
  void initState() {
    super.initState();
    _tabController = TabController(length: 2, vsync: this);
  }

  @override
  void dispose() {
    _searchController.dispose();
    _tabController.dispose();
    _debounceTimer?.cancel();
    super.dispose();
  }

  void _onSearchChanged(String value) {
    if (_debounceTimer?.isActive ?? false) _debounceTimer!.cancel();
    _debounceTimer = Timer(const Duration(milliseconds: 500), () {
      ref.read(prSearchQueryProvider.notifier).state = value;
    });
  }

  Future<void> _refresh() async {
    setState(() => _isRefreshing = true);

    // Refresh main lists first
    await Future.wait([
      ref.read(prListProvider.notifier).refresh(),
      ref.read(createdPrListProvider.notifier).refresh(),
    ]);

    setState(() => _isRefreshing = false);

    // Refresh background lists
    ref.read(reviewedPrListProvider.notifier).refresh();
    ref.read(recentlyCreatedPrListProvider.notifier).refresh();
  }

  void _showLogoutDialog() {
    showDialog(
      context: context,
      builder: (context) => AlertDialog(
        backgroundColor: Theme.of(context).colorScheme.surface,
        title: const Text('Sign out'),
        content: const Text('Are you sure you want to sign out?'),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context),
            child: const Text('Cancel'),
          ),
          ElevatedButton(
            onPressed: () {
              Navigator.pop(context);
              ref.read(authProvider.notifier).logout();
            },
            style: ElevatedButton.styleFrom(
              backgroundColor: Theme.of(context).colorScheme.error,
            ),
            child: const Text('Sign out'),
          ),
        ],
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final username = ref.watch(currentUsernameProvider);
    final themeMode = ref.watch(themeModeProvider);
    final isDark = themeMode == ThemeMode.dark;
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;

    return Scaffold(
      body: Column(
        children: [
          // Header
          Container(
            padding: const EdgeInsets.all(AppConstants.defaultPadding),
            decoration: BoxDecoration(
              color: colorScheme.surface,
              border: Border(
                bottom: BorderSide(color: colorScheme.outline),
              ),
            ),
            child: Row(
              children: [
                // App icon
                Icon(
                  Icons.inbox_rounded,
                  color: colorScheme.primary,
                  size: 24,
                ),
                const SizedBox(width: 12),

                // Tabs
                Container(
                  decoration: BoxDecoration(
                    color: theme.cardTheme.color,
                    borderRadius: BorderRadius.circular(8),
                    border: Border.all(color: colorScheme.outline),
                  ),
                  child: TabBar(
                    controller: _tabController,
                    isScrollable: true,
                    tabAlignment: TabAlignment.start,
                    indicatorSize: TabBarIndicatorSize.tab,
                    dividerColor: Colors.transparent,
                    indicator: BoxDecoration(
                      color: colorScheme.primary.withValues(alpha: 0.15),
                      borderRadius: BorderRadius.circular(6),
                    ),
                    labelColor: colorScheme.primary,
                    unselectedLabelColor: theme.textTheme.bodyMedium?.color,
                    labelStyle: const TextStyle(
                      fontSize: 13,
                      fontWeight: FontWeight.w600,
                    ),
                    unselectedLabelStyle: const TextStyle(
                      fontSize: 13,
                      fontWeight: FontWeight.w500,
                    ),
                    padding: const EdgeInsets.all(4),
                    labelPadding: const EdgeInsets.symmetric(horizontal: 16),
                    tabs: const [
                      Tab(
                        height: 32,
                        text: 'Review Requests',
                      ),
                      Tab(
                        height: 32,
                        text: 'Created',
                      ),
                    ],
                  ),
                ),

                const Spacer(),

                // Search field
                SizedBox(
                  width: 220,
                  height: 36,
                  child: TextField(
                    controller: _searchController,
                    onChanged: _onSearchChanged,
                    decoration: InputDecoration(
                      hintText: 'Search...',
                      prefixIcon: const Icon(Icons.search, size: 18),
                      contentPadding: const EdgeInsets.symmetric(horizontal: 12),
                      border: OutlineInputBorder(
                        borderRadius: BorderRadius.circular(8),
                        borderSide: BorderSide(color: colorScheme.outline),
                      ),
                      enabledBorder: OutlineInputBorder(
                        borderRadius: BorderRadius.circular(8),
                        borderSide: BorderSide(color: colorScheme.outline),
                      ),
                      filled: true,
                      fillColor: theme.cardTheme.color,
                    ),
                    style: const TextStyle(fontSize: 13),
                  ),
                ),

                const SizedBox(width: AppConstants.defaultPadding),
                
                // Theme Toggle
                IconButton(
                  onPressed: () {
                    ref.read(themeModeProvider.notifier).toggle();
                  },
                  icon: Icon(
                    isDark ? Icons.light_mode_rounded : Icons.dark_mode_rounded,
                    size: 20,
                  ),
                  tooltip: isDark ? 'Switch to Light Mode' : 'Switch to Dark Mode',
                ),

                // Refresh button
                IconButton(
                  onPressed: _isRefreshing ? null : _refresh,
                  icon: _isRefreshing
                      ? const SizedBox(
                          width: 18,
                          height: 18,
                          child: CircularProgressIndicator(strokeWidth: 2),
                        )
                      : const Icon(Icons.refresh_rounded),
                  tooltip: 'Refresh',
                ),

                // User menu
                PopupMenuButton<String>(
                  offset: const Offset(0, 40),
                  shape: RoundedRectangleBorder(
                    borderRadius: BorderRadius.circular(8),
                    side: BorderSide(color: colorScheme.outline),
                  ),
                  color: colorScheme.surface,
                  child: Container(
                    padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
                    decoration: BoxDecoration(
                      color: theme.cardTheme.color,
                      borderRadius: BorderRadius.circular(8),
                      border: Border.all(color: colorScheme.outline),
                    ),
                    child: Row(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        Icon(
                          Icons.person_outline,
                          size: 18,
                          color: theme.textTheme.bodyMedium?.color,
                        ),
                        const SizedBox(width: 6),
                        username.when(
                          data: (name) => Text(
                            name ?? 'User',
                            style: theme.textTheme.bodyMedium,
                          ),
                          loading: () => const Text('...'),
                          error: (e, s) => const Text('User'),
                        ),
                        const SizedBox(width: 4),
                        Icon(
                          Icons.expand_more,
                          size: 18,
                          color: theme.textTheme.bodySmall?.color,
                        ),
                      ],
                    ),
                  ),
                  itemBuilder: (context) => [
                    PopupMenuItem<String>(
                      value: 'logout',
                      child: Row(
                        children: [
                          Icon(
                            Icons.logout_rounded,
                            size: 18,
                            color: colorScheme.error,
                          ),
                          const SizedBox(width: 8),
                          Text(
                            'Sign out',
                            style: TextStyle(color: colorScheme.error),
                          ),
                        ],
                      ),
                    ),
                  ],
                  onSelected: (value) {
                    if (value == 'logout') {
                      _showLogoutDialog();
                    }
                  },
                ),
              ],
            ),
          ),

          // Tab content
          Expanded(
            child: TabBarView(
              controller: _tabController,
              children: [
                _buildReviewRequestsTab(),
                _buildCreatedTab(),
              ],
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildReviewRequestsTab() {
    final pendingReviews = ref.watch(filteredPrListProvider);
    final reviewedPrs = ref.watch(reviewedPrListProvider);

    return pendingReviews.when(
      data: (prs) {
        return PaginatedPrList(
          prs: prs,
          onLoadMore: () => ref.read(prListProvider.notifier).loadMore(),
          header: _buildSectionHeader('Pending Reviews', Icons.pending_actions_rounded),
          footer: _buildRecentlyReviewedSection(reviewedPrs),
          emptyMessage: 'No pull requests waiting for your review',
          emptyIcon: Icons.check_circle_outline_rounded,
        );
      },
      loading: () => _buildLoadingState(),
      error: (error, stack) => _buildErrorState(error),
    );
  }

  Widget _buildRecentlyReviewedSection(
      AsyncValue<List<ReviewedPullRequestModel>> reviewedPrs) {
    return reviewedPrs.when(
      data: (prs) {
        if (prs.isEmpty) return const SizedBox.shrink();
        
        final theme = Theme.of(context);
        
        return Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Divider(color: theme.colorScheme.outline),
            const SizedBox(height: AppConstants.defaultPadding),
            Row(
              children: [
                Icon(Icons.history_rounded, size: 16, color: theme.textTheme.bodySmall?.color),
                const SizedBox(width: 8),
                Text(
                  'Recently Reviewed',
                  style: theme.textTheme.titleSmall?.copyWith(
                        color: theme.textTheme.bodyMedium?.color,
                        fontWeight: FontWeight.w600,
                      ),
                ),
              ],
            ),
            const SizedBox(height: AppConstants.smallPadding),
            Column(
              children: [
                for (final pr in prs) ...[
                  ReviewedPrCard(pr: pr),
                  const SizedBox(height: 6),
                ],
              ],
            ),
          ],
        );
      },
      loading: () => const SizedBox.shrink(),
      error: (_, __) => const SizedBox.shrink(),
    );
  }

  Widget _buildCreatedTab() {
    final createdPrs = ref.watch(filteredCreatedPrListProvider);
    final recentlyCreatedPrs = ref.watch(recentlyCreatedPrListProvider);

    return createdPrs.when(
      data: (prs) {
        return PaginatedPrList(
          prs: prs,
          onLoadMore: () => ref.read(createdPrListProvider.notifier).loadMore(),
          header: _buildSectionHeader('Open Pull Requests', Icons.folder_open_rounded),
          footer: _buildRecentlyCreatedSection(recentlyCreatedPrs),
          emptyMessage: 'You have no open pull requests',
          emptyIcon: Icons.create_rounded,
        );
      },
      loading: () => _buildLoadingState(),
      error: (error, stack) => _buildErrorState(error),
    );
  }

  Widget _buildRecentlyCreatedSection(
      AsyncValue<List<CreatedPullRequestModel>> recentlyCreatedPrs) {
    return recentlyCreatedPrs.when(
      data: (prs) {
        if (prs.isEmpty) return const SizedBox.shrink();
        
        final theme = Theme.of(context);
        
        return Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Divider(color: theme.colorScheme.outline),
            const SizedBox(height: AppConstants.defaultPadding),
            Row(
              children: [
                Icon(Icons.history_rounded, size: 16, color: theme.textTheme.bodySmall?.color),
                const SizedBox(width: 8),
                Text(
                  'Recently Created',
                  style: theme.textTheme.titleSmall?.copyWith(
                        color: theme.textTheme.bodyMedium?.color,
                        fontWeight: FontWeight.w600,
                      ),
                ),
              ],
            ),
            const SizedBox(height: AppConstants.smallPadding),
            Column(
              children: [
                for (final pr in prs) ...[
                  CreatedPrCard(pr: pr),
                  const SizedBox(height: 6),
                ],
              ],
            ),
          ],
        );
      },
      loading: () => const SizedBox.shrink(),
      error: (_, __) => const SizedBox.shrink(),
    );
  }

  Widget _buildSectionHeader(String title, IconData icon) {
    final theme = Theme.of(context);
    return Row(
      children: [
        Icon(icon, size: 16, color: theme.textTheme.bodySmall?.color),
        const SizedBox(width: 8),
        Text(
          title,
          style: theme.textTheme.titleSmall?.copyWith(
                color: theme.textTheme.bodyMedium?.color,
                fontWeight: FontWeight.w600,
              ),
        ),
      ],
    );
  }

  Widget _buildLoadingState() {
    return Center(
      child: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          const CircularProgressIndicator(),
          const SizedBox(height: AppConstants.defaultPadding),
          Text(
            'Loading pull requests...',
            style: Theme.of(context).textTheme.bodyMedium,
          ),
        ],
      ),
    );
  }

  Widget _buildErrorState(Object error) {
    final theme = Theme.of(context);
    return Center(
      child: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          Icon(
            Icons.error_outline_rounded,
            size: 64,
            color: theme.colorScheme.error,
          ),
          const SizedBox(height: AppConstants.defaultPadding),
          Text(
            'Something went wrong',
            style: theme.textTheme.titleLarge?.copyWith(
                  color: theme.textTheme.bodyMedium?.color,
                ),
          ),
          const SizedBox(height: AppConstants.smallPadding),
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: 48),
            child: Text(
              error.toString(),
              style: theme.textTheme.bodyMedium,
              textAlign: TextAlign.center,
            ),
          ),
          const SizedBox(height: AppConstants.defaultPadding),
          ElevatedButton.icon(
            onPressed: _refresh,
            icon: const Icon(Icons.refresh_rounded, size: 18),
            label: const Text('Try again'),
          ),
        ],
      ),
    );
  }
}
