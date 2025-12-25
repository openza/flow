import 'package:cached_network_image/cached_network_image.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../auth/presentation/providers/auth_provider.dart';
import '../providers/pr_provider.dart';

class OrgFilterDropdown extends ConsumerWidget {
  const OrgFilterDropdown({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;
    final orgsAsync = ref.watch(userOrganizationsProvider);
    final selectedOrg = ref.watch(selectedOrgProvider);

    return orgsAsync.when(
      data: (orgs) {
        if (orgs.isEmpty) {
          return const SizedBox.shrink();
        }

        return PopupMenuButton<String>(
          offset: const Offset(0, 40),
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(8),
            side: BorderSide(color: colorScheme.outline),
          ),
          color: colorScheme.surface,
          initialValue: selectedOrg ?? '',
          onSelected: (value) {
            // Empty string means "All Organizations"
            ref.read(selectedOrgProvider.notifier).setOrg(value.isEmpty ? null : value);
          },
          child: Container(
            padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
            constraints: const BoxConstraints(maxWidth: 180),
            decoration: BoxDecoration(
              color: theme.cardTheme.color,
              borderRadius: BorderRadius.circular(8),
              border: Border.all(color: colorScheme.outline),
            ),
            child: Row(
              mainAxisSize: MainAxisSize.min,
              children: [
                Icon(
                  Icons.business_rounded,
                  size: 16,
                  color: theme.textTheme.bodyMedium?.color,
                ),
                const SizedBox(width: 6),
                Flexible(
                  child: Text(
                    selectedOrg ?? 'All Organizations',
                    style: theme.textTheme.bodyMedium,
                    overflow: TextOverflow.ellipsis,
                  ),
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
            // "All Organizations" option
            PopupMenuItem<String>(
              value: '',
              child: Row(
                children: [
                  Icon(
                    Icons.all_inclusive_rounded,
                    size: 18,
                    color: selectedOrg == null ? colorScheme.primary : theme.textTheme.bodyMedium?.color,
                  ),
                  const SizedBox(width: 8),
                  Text(
                    'All Organizations',
                    style: TextStyle(
                      color: selectedOrg == null ? colorScheme.primary : null,
                      fontWeight: selectedOrg == null ? FontWeight.w600 : null,
                    ),
                  ),
                ],
              ),
            ),
            const PopupMenuDivider(),
            // Organization items
            ...orgs.map((org) => PopupMenuItem<String>(
              value: org.login,
              child: Row(
                children: [
                  ClipRRect(
                    borderRadius: BorderRadius.circular(4),
                    child: CachedNetworkImage(
                      imageUrl: org.avatarUrl,
                      width: 18,
                      height: 18,
                      placeholder: (context, url) => Container(
                        width: 18,
                        height: 18,
                        color: colorScheme.surfaceContainerHighest,
                      ),
                      errorWidget: (context, url, error) => Icon(
                        Icons.business_rounded,
                        size: 18,
                        color: theme.textTheme.bodyMedium?.color,
                      ),
                    ),
                  ),
                  const SizedBox(width: 8),
                  Expanded(
                    child: Text(
                      org.name.isNotEmpty ? org.name : org.login,
                      overflow: TextOverflow.ellipsis,
                      style: TextStyle(
                        color: selectedOrg == org.login ? colorScheme.primary : null,
                        fontWeight: selectedOrg == org.login ? FontWeight.w600 : null,
                      ),
                    ),
                  ),
                ],
              ),
            )),
          ],
        );
      },
      loading: () => Container(
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
        decoration: BoxDecoration(
          color: theme.cardTheme.color,
          borderRadius: BorderRadius.circular(8),
          border: Border.all(color: colorScheme.outline),
        ),
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            SizedBox(
              width: 16,
              height: 16,
              child: CircularProgressIndicator(
                strokeWidth: 2,
                color: theme.textTheme.bodySmall?.color,
              ),
            ),
            const SizedBox(width: 8),
            Text(
              'Loading...',
              style: theme.textTheme.bodyMedium,
            ),
          ],
        ),
      ),
      error: (_, __) => const SizedBox.shrink(),
    );
  }
}
