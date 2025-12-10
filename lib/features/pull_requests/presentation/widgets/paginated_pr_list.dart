import 'package:flutter/material.dart';
import '../../../../core/constants/app_constants.dart';
import '../../../../core/theme/app_theme.dart';
import '../../domain/models/pull_request.dart';
import 'pr_card.dart';

class PaginatedPrList extends StatelessWidget {
  final List<PullRequestModel> prs;
  final VoidCallback onLoadMore;
  final Widget? header;
  final Widget? footer;
  final String emptyMessage;
  final IconData emptyIcon;

  const PaginatedPrList({
    super.key,
    required this.prs,
    required this.onLoadMore,
    this.header,
    this.footer,
    required this.emptyMessage,
    required this.emptyIcon,
  });

  @override
  Widget build(BuildContext context) {
    if (prs.isEmpty) {
      return ListView(
        padding: const EdgeInsets.all(AppConstants.defaultPadding),
        children: [
          if (header != null) header!,
          if (header != null) const SizedBox(height: AppConstants.defaultPadding),
          _buildInlineEmptyState(context, emptyMessage, emptyIcon),
          if (footer != null) const SizedBox(height: AppConstants.defaultPadding),
          if (footer != null) footer!,
        ],
      );
    }

    return NotificationListener<ScrollNotification>(
      onNotification: (scrollInfo) {
        if (scrollInfo.metrics.pixels >= scrollInfo.metrics.maxScrollExtent - 200) {
          onLoadMore();
        }
        return false;
      },
      child: ListView.builder(
        padding: const EdgeInsets.all(AppConstants.defaultPadding),
        itemCount: prs.length + (header != null ? 1 : 0) + (footer != null ? 1 : 0),
        itemBuilder: (context, index) {
          final hasHeader = header != null;
          final hasFooter = footer != null;

          if (hasHeader && index == 0) {
            return Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                header!,
                const SizedBox(height: AppConstants.defaultPadding),
              ],
            );
          }

          if (hasFooter && index == (prs.length + (hasHeader ? 1 : 0))) {
            return Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                const SizedBox(height: AppConstants.defaultPadding),
                footer!,
              ],
            );
          }

          final prIndex = hasHeader ? index - 1 : index;
          return Column(
            children: [
              PrCard(pr: prs[prIndex], index: prIndex),
              if (prIndex == prs.length - 1 && !hasFooter) 
                const SizedBox(height: AppConstants.defaultPadding),
            ],
          );
        },
      ),
    );
  }

  Widget _buildInlineEmptyState(BuildContext context, String message, IconData icon) {
    final theme = Theme.of(context);
    return Container(
      padding: const EdgeInsets.symmetric(vertical: 48),
      child: Column(
        children: [
          Icon(
            icon,
            size: 48,
            color: theme.textTheme.bodySmall?.color,
          ),
          const SizedBox(height: 12),
          Text(
            'All caught up!',
            style: theme.textTheme.titleMedium?.copyWith(
                  color: theme.textTheme.bodyMedium?.color,
                ),
          ),
          const SizedBox(height: 4),
          Text(
            message,
            style: theme.textTheme.bodyMedium?.copyWith(
                  color: theme.textTheme.bodySmall?.color,
                ),
          ),
        ],
      ),
    );
  }
}
