import 'package:cached_network_image/cached_network_image.dart';
import 'package:flutter/material.dart';
import 'package:timeago/timeago.dart' as timeago;
import 'package:url_launcher/url_launcher.dart';

import '../../../../core/constants/app_constants.dart';
import '../../domain/models/pull_request.dart';

class PrCard extends StatefulWidget {
  final PullRequestModel pr;
  final int index;

  const PrCard({
    super.key,
    required this.pr,
    this.index = 0,
  });

  @override
  State<PrCard> createState() => _PrCardState();
}

class _PrCardState extends State<PrCard> with SingleTickerProviderStateMixin {
  bool _isHovered = false;
  late AnimationController _animationController;
  late Animation<double> _fadeAnimation;
  late Animation<Offset> _slideAnimation;

  @override
  void initState() {
    super.initState();
    _animationController = AnimationController(
      duration: AppConstants.mediumAnimation,
      vsync: this,
    );

    _fadeAnimation = Tween<double>(begin: 0.0, end: 1.0).animate(
      CurvedAnimation(parent: _animationController, curve: Curves.easeOut),
    );

    _slideAnimation = Tween<Offset>(
      begin: const Offset(0, 0.1),
      end: Offset.zero,
    ).animate(
      CurvedAnimation(parent: _animationController, curve: Curves.easeOut),
    );

    // Staggered animation based on index
    Future.delayed(Duration(milliseconds: 50 * widget.index), () {
      if (mounted) {
        _animationController.forward();
      }
    });
  }

  @override
  void dispose() {
    _animationController.dispose();
    super.dispose();
  }

  Future<void> _openPr() async {
    final uri = Uri.parse(widget.pr.htmlUrl);
    if (await canLaunchUrl(uri)) {
      await launchUrl(uri, mode: LaunchMode.externalApplication);
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;
    final textTheme = theme.textTheme;

    return FadeTransition(
      opacity: _fadeAnimation,
      child: SlideTransition(
        position: _slideAnimation,
        child: MouseRegion(
          onEnter: (_) => setState(() => _isHovered = true),
          onExit: (_) => setState(() => _isHovered = false),
          cursor: SystemMouseCursors.click,
          child: GestureDetector(
            onTap: _openPr,
            child: AnimatedContainer(
              duration: AppConstants.shortAnimation,
              margin: const EdgeInsets.only(bottom: AppConstants.smallPadding),
              decoration: BoxDecoration(
                color: _isHovered ? colorScheme.surface : theme.cardTheme.color,
                borderRadius: BorderRadius.circular(AppConstants.cardBorderRadius),
                border: Border.all(
                  color: _isHovered ? colorScheme.primary.withValues(alpha: 0.5) : colorScheme.outline,
                  width: 1,
                ),
              ),
              child: Padding(
                padding: const EdgeInsets.all(AppConstants.defaultPadding),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    // Repository name row
                    Row(
                      children: [
                        Icon(
                          Icons.book_outlined,
                          size: 14,
                          color: textTheme.bodySmall?.color,
                        ),
                        const SizedBox(width: 6),
                        Expanded(
                          child: Text(
                            widget.pr.repository.fullName,
                            style: textTheme.bodySmall?.copyWith(
                                  color: textTheme.bodyMedium?.color,
                                ),
                            overflow: TextOverflow.ellipsis,
                          ),
                        ),
                        // Time ago
                        Text(
                          timeago.format(widget.pr.updatedAt),
                          style: textTheme.bodySmall,
                        ),
                      ],
                    ),

                    const SizedBox(height: AppConstants.smallPadding),

                    // PR title row
                    Row(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        // PR icon
                        Container(
                          margin: const EdgeInsets.only(top: 2),
                          child: Icon(
                            widget.pr.draft
                                ? Icons.edit_document
                                : Icons.merge_rounded,
                            size: 16,
                            color: widget.pr.draft
                                ? textTheme.bodySmall?.color
                                : colorScheme.secondary, // Green
                          ),
                        ),
                        const SizedBox(width: 8),
                        // Title and number
                        Expanded(
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              Text.rich(
                                TextSpan(
                                  children: [
                                    TextSpan(
                                      text: widget.pr.title,
                                      style: textTheme.titleMedium?.copyWith(
                                            color: _isHovered
                                                ? colorScheme.primary
                                                : textTheme.bodyLarge?.color,
                                          ),
                                    ),
                                    TextSpan(
                                      text: ' #${widget.pr.number}',
                                      style: textTheme.bodyMedium?.copyWith(
                                            color: textTheme.bodySmall?.color,
                                          ),
                                    ),
                                  ],
                                ),
                                maxLines: 2,
                                overflow: TextOverflow.ellipsis,
                              ),
                            ],
                          ),
                        ),
                      ],
                    ),

                    const SizedBox(height: AppConstants.smallPadding + 4),

                    // Author and labels row
                    Row(
                      children: [
                        // Author avatar
                        ClipRRect(
                          borderRadius: BorderRadius.circular(AppConstants.smallAvatarSize / 2),
                          child: CachedNetworkImage(
                            imageUrl: widget.pr.author.avatarUrl,
                            width: AppConstants.smallAvatarSize,
                            height: AppConstants.smallAvatarSize,
                            placeholder: (context, url) => Container(
                              width: AppConstants.smallAvatarSize,
                              height: AppConstants.smallAvatarSize,
                              color: colorScheme.surface,
                            ),
                            errorWidget: (context, url, error) => Container(
                              width: AppConstants.smallAvatarSize,
                              height: AppConstants.smallAvatarSize,
                              color: colorScheme.surface,
                              child: const Icon(Icons.person, size: 12),
                            ),
                          ),
                        ),
                        const SizedBox(width: 6),
                        Text(
                          widget.pr.author.login,
                          style: textTheme.bodySmall,
                        ),

                        // Draft badge
                        if (widget.pr.draft) ...[
                          const SizedBox(width: 8),
                          Container(
                            padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
                            decoration: BoxDecoration(
                              color: colorScheme.surface,
                              borderRadius: BorderRadius.circular(4),
                              border: Border.all(color: colorScheme.outline),
                            ),
                            child: Text(
                              'Draft',
                              style: textTheme.bodySmall?.copyWith(
                                    fontSize: 10,
                                    fontWeight: FontWeight.w500,
                                  ),
                            ),
                          ),
                        ],

                        const Spacer(),

                        // Labels
                        if (widget.pr.labels.isNotEmpty)
                          Flexible(
                            child: Row(
                              mainAxisSize: MainAxisSize.min,
                              children: widget.pr.labels.take(3).map((label) {
                                return Padding(
                                  padding: const EdgeInsets.only(left: 4),
                                  child: _LabelChip(label: label),
                                );
                              }).toList(),
                            ),
                          ),
                      ],
                    ),
                  ],
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _LabelChip extends StatelessWidget {
  final LabelModel label;

  const _LabelChip({required this.label});

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
      decoration: BoxDecoration(
        color: label.backgroundColor.withValues(alpha: 0.2),
        borderRadius: BorderRadius.circular(12),
        border: Border.all(
          color: label.backgroundColor.withValues(alpha: 0.5),
          width: 1,
        ),
      ),
      child: Text(
        label.name,
        style: TextStyle(
          color: label.backgroundColor,
          fontSize: 10,
          fontWeight: FontWeight.w500,
        ),
        maxLines: 1,
        overflow: TextOverflow.ellipsis,
      ),
    );
  }
}

/// Compact card for displaying reviewed PRs with review and merge status
class ReviewedPrCard extends StatefulWidget {
  final ReviewedPullRequestModel pr;

  const ReviewedPrCard({
    super.key,
    required this.pr,
  });

  @override
  State<ReviewedPrCard> createState() => _ReviewedPrCardState();
}

class _ReviewedPrCardState extends State<ReviewedPrCard> {
  bool _isHovered = false;

  Future<void> _openPr() async {
    final uri = Uri.parse(widget.pr.htmlUrl);
    if (await canLaunchUrl(uri)) {
      await launchUrl(uri, mode: LaunchMode.externalApplication);
    }
  }

  Color _getReviewStateColor(ColorScheme scheme) {
    switch (widget.pr.reviewState) {
      case ReviewState.approved:
        return scheme.secondary; // Success
      case ReviewState.changesRequested:
        return scheme.error;
      case ReviewState.commented:
        return scheme.tertiary; // Warning
      case ReviewState.pending:
        return scheme.onSurface.withValues(alpha: 0.6); // Muted
    }
  }

  IconData _getReviewStateIcon() {
    switch (widget.pr.reviewState) {
      case ReviewState.approved:
        return Icons.check_circle;
      case ReviewState.changesRequested:
        return Icons.change_circle;
      case ReviewState.commented:
        return Icons.comment;
      case ReviewState.pending:
        return Icons.pending;
    }
  }

  String _getReviewStateText() {
    switch (widget.pr.reviewState) {
      case ReviewState.approved:
        return 'Approved';
      case ReviewState.changesRequested:
        return 'Changes';
      case ReviewState.commented:
        return 'Commented';
      case ReviewState.pending:
        return 'Pending';
    }
  }

  Color _getMergeStateColor(ColorScheme scheme) {
    switch (widget.pr.mergeState) {
      case MergeState.merged:
        return scheme.primary; // Blue/purple for merged
      case MergeState.open:
        return scheme.tertiary; // Warning/Yellow for open
      case MergeState.closed:
        return scheme.error; // Red for closed
    }
  }

  IconData _getMergeStateIcon() {
    switch (widget.pr.mergeState) {
      case MergeState.merged:
        return Icons.merge;
      case MergeState.open:
        return Icons.circle_outlined;
      case MergeState.closed:
        return Icons.cancel_outlined;
    }
  }

  String _getMergeStateText() {
    switch (widget.pr.mergeState) {
      case MergeState.merged:
        return 'Merged';
      case MergeState.open:
        return 'Open';
      case MergeState.closed:
        return 'Closed';
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;
    final textTheme = theme.textTheme;

    return MouseRegion(
      onEnter: (_) => setState(() => _isHovered = true),
      onExit: (_) => setState(() => _isHovered = false),
      cursor: SystemMouseCursors.click,
      child: GestureDetector(
        onTap: _openPr,
        child: AnimatedContainer(
          duration: AppConstants.shortAnimation,
          padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
          decoration: BoxDecoration(
            color: _isHovered ? colorScheme.surface : theme.cardTheme.color,
            borderRadius: BorderRadius.circular(8),
            border: Border.all(
              color: _isHovered ? colorScheme.primary.withValues(alpha: 0.5) : colorScheme.outline,
            ),
          ),
          child: Row(
            children: [
              // Merge state icon
              Icon(
                _getMergeStateIcon(),
                size: 16,
                color: _getMergeStateColor(colorScheme),
              ),
              const SizedBox(width: 10),

              // PR info
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    // Title
                    Text(
                      widget.pr.title,
                      style: textTheme.bodyMedium?.copyWith(
                            color: _isHovered ? colorScheme.primary : textTheme.bodyLarge?.color,
                            fontWeight: FontWeight.w500,
                          ),
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                    ),
                    const SizedBox(height: 2),
                    // Repo and PR number
                    Text(
                      '${widget.pr.repository.fullName} #${widget.pr.number}',
                      style: textTheme.bodySmall?.copyWith(
                            color: textTheme.bodySmall?.color,
                            fontSize: 11,
                          ),
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                    ),
                  ],
                ),
              ),

              const SizedBox(width: 12),

              // Review status badge - fixed width
              SizedBox(
                width: 90,
                child: Container(
                  padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
                  decoration: BoxDecoration(
                    color: _getReviewStateColor(colorScheme).withValues(alpha: 0.15),
                    borderRadius: BorderRadius.circular(4),
                    border: Border.all(
                      color: _getReviewStateColor(colorScheme).withValues(alpha: 0.3),
                    ),
                  ),
                  child: Row(
                    mainAxisSize: MainAxisSize.min,
                    mainAxisAlignment: MainAxisAlignment.center,
                    children: [
                      Icon(
                        _getReviewStateIcon(),
                        size: 12,
                        color: _getReviewStateColor(colorScheme),
                      ),
                      const SizedBox(width: 4),
                      Text(
                        _getReviewStateText(),
                        style: TextStyle(
                          color: _getReviewStateColor(colorScheme),
                          fontSize: 11,
                          fontWeight: FontWeight.w500,
                        ),
                      ),
                    ],
                  ),
                ),
              ),

              const SizedBox(width: 8),

              // Merge status badge - fixed width
              SizedBox(
                width: 60,
                child: Container(
                  padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
                  decoration: BoxDecoration(
                    color: _getMergeStateColor(colorScheme).withValues(alpha: 0.15),
                    borderRadius: BorderRadius.circular(4),
                    border: Border.all(
                      color: _getMergeStateColor(colorScheme).withValues(alpha: 0.3),
                    ),
                  ),
                  child: Text(
                    _getMergeStateText(),
                    style: TextStyle(
                      color: _getMergeStateColor(colorScheme),
                      fontSize: 11,
                      fontWeight: FontWeight.w500,
                    ),
                    textAlign: TextAlign.center,
                  ),
                ),
              ),

              const SizedBox(width: 12),

              // Author with avatar - fixed width
              SizedBox(
                width: 140,
                child: Row(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    ClipRRect(
                      borderRadius: BorderRadius.circular(10),
                      child: CachedNetworkImage(
                        imageUrl: widget.pr.author.avatarUrl,
                        width: 20,
                        height: 20,
                        placeholder: (context, url) => Container(
                          width: 20,
                          height: 20,
                          color: colorScheme.surface,
                        ),
                        errorWidget: (context, url, error) => Container(
                          width: 20,
                          height: 20,
                          color: colorScheme.surface,
                          child: const Icon(Icons.person, size: 12),
                        ),
                      ),
                    ),
                    const SizedBox(width: 6),
                    Expanded(
                      child: Text(
                        widget.pr.author.login,
                        style: textTheme.bodySmall?.copyWith(
                              color: textTheme.bodyMedium?.color,
                              fontSize: 11,
                            ),
                        overflow: TextOverflow.ellipsis,
                      ),
                    ),
                  ],
                ),
              ),

              const SizedBox(width: 8),

              // Time ago - fixed width
              SizedBox(
                width: 75,
                child: Text(
                  timeago.format(widget.pr.reviewedAt),
                  style: textTheme.bodySmall?.copyWith(
                        color: textTheme.bodySmall?.color,
                        fontSize: 11,
                      ),
                  textAlign: TextAlign.right,
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

/// Compact card for displaying recently created PRs with merge status
class CreatedPrCard extends StatefulWidget {
  final CreatedPullRequestModel pr;

  const CreatedPrCard({
    super.key,
    required this.pr,
  });

  @override
  State<CreatedPrCard> createState() => _CreatedPrCardState();
}

class _CreatedPrCardState extends State<CreatedPrCard> {
  bool _isHovered = false;

  Future<void> _openPr() async {
    final uri = Uri.parse(widget.pr.htmlUrl);
    if (await canLaunchUrl(uri)) {
      await launchUrl(uri, mode: LaunchMode.externalApplication);
    }
  }

  Color _getMergeStateColor(ColorScheme scheme) {
    switch (widget.pr.mergeState) {
      case MergeState.merged:
        return scheme.primary; // Blue/purple for merged
      case MergeState.open:
        return scheme.tertiary; // Warning/Yellow for open
      case MergeState.closed:
        return scheme.error; // Red for closed
    }
  }

  IconData _getMergeStateIcon() {
    switch (widget.pr.mergeState) {
      case MergeState.merged:
        return Icons.merge;
      case MergeState.open:
        return Icons.circle_outlined;
      case MergeState.closed:
        return Icons.cancel_outlined;
    }
  }

  String _getMergeStateText() {
    switch (widget.pr.mergeState) {
      case MergeState.merged:
        return 'Merged';
      case MergeState.open:
        return 'Open';
      case MergeState.closed:
        return 'Closed';
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;
    final textTheme = theme.textTheme;

    return MouseRegion(
      onEnter: (_) => setState(() => _isHovered = true),
      onExit: (_) => setState(() => _isHovered = false),
      cursor: SystemMouseCursors.click,
      child: GestureDetector(
        onTap: _openPr,
        child: AnimatedContainer(
          duration: AppConstants.shortAnimation,
          padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
          decoration: BoxDecoration(
            color: _isHovered ? colorScheme.surface : theme.cardTheme.color,
            borderRadius: BorderRadius.circular(8),
            border: Border.all(
              color: _isHovered ? colorScheme.primary.withValues(alpha: 0.5) : colorScheme.outline,
            ),
          ),
          child: Row(
            children: [
              // Merge state icon
              Icon(
                _getMergeStateIcon(),
                size: 16,
                color: _getMergeStateColor(colorScheme),
              ),
              const SizedBox(width: 10),

              // PR info
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    // Title
                    Text(
                      widget.pr.title,
                      style: textTheme.bodyMedium?.copyWith(
                            color: _isHovered ? colorScheme.primary : textTheme.bodyLarge?.color,
                            fontWeight: FontWeight.w500,
                          ),
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                    ),
                    const SizedBox(height: 2),
                    // Repo and PR number
                    Text(
                      '${widget.pr.repository.fullName} #${widget.pr.number}',
                      style: textTheme.bodySmall?.copyWith(
                            color: textTheme.bodySmall?.color,
                            fontSize: 11,
                          ),
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                    ),
                  ],
                ),
              ),

              const SizedBox(width: 12),

              // Merge status badge - fixed width
              SizedBox(
                width: 70,
                child: Container(
                  padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
                  decoration: BoxDecoration(
                    color: _getMergeStateColor(colorScheme).withValues(alpha: 0.15),
                    borderRadius: BorderRadius.circular(4),
                    border: Border.all(
                      color: _getMergeStateColor(colorScheme).withValues(alpha: 0.3),
                    ),
                  ),
                  child: Text(
                    _getMergeStateText(),
                    style: TextStyle(
                      color: _getMergeStateColor(colorScheme),
                      fontSize: 11,
                      fontWeight: FontWeight.w500,
                    ),
                    textAlign: TextAlign.center,
                  ),
                ),
              ),

              const SizedBox(width: 12),

              // Time ago - fixed width
              SizedBox(
                width: 75,
                child: Text(
                  timeago.format(widget.pr.createdAt),
                  style: textTheme.bodySmall?.copyWith(
                        color: textTheme.bodySmall?.color,
                        fontSize: 11,
                      ),
                  textAlign: TextAlign.right,
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
