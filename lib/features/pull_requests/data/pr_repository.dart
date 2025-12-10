import 'dart:convert';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:http/http.dart' as http;

import '../../../core/constants/app_constants.dart';
import '../../auth/data/token_repository.dart';
import '../domain/models/pull_request.dart';

final prRepositoryProvider = Provider<PrRepository>((ref) {
  final tokenRepo = ref.watch(tokenRepositoryProvider);
  return PrRepository(tokenRepo);
});

class PrRepository {
  final TokenRepository _tokenRepository;

  PrRepository(this._tokenRepository);

  Future<List<PullRequestModel>> getReviewRequests() async {
    final token = await _tokenRepository.getToken();
    final username = await _tokenRepository.getUsername();

    if (token == null || username == null) {
      throw Exception('Not authenticated');
    }

    final query = 'type:pr state:open review-requested:$username';
    final uri = Uri.parse(
      '${AppConstants.githubApiBaseUrl}/search/issues?q=${Uri.encodeComponent(query)}&sort=updated&order=desc&per_page=100',
    );

    final response = await http.get(
      uri,
      headers: {
        'Authorization': 'Bearer $token',
        'Accept': 'application/vnd.github+json',
        'X-GitHub-Api-Version': '2022-11-28',
      },
    ).timeout(const Duration(seconds: 30));

    if (response.statusCode == 200) {
      final data = json.decode(response.body) as Map<String, dynamic>;
      final items = data['items'] as List<dynamic>? ?? [];

      return items
          .map((item) =>
              PullRequestModel.fromGitHubIssue(item as Map<String, dynamic>))
          .toList();
    } else if (response.statusCode == 401) {
      throw Exception('Authentication failed. Please check your token.');
    } else if (response.statusCode == 403) {
      final remaining = response.headers['x-ratelimit-remaining'];
      if (remaining == '0') {
        final resetTime = response.headers['x-ratelimit-reset'];
        throw Exception(
            'Rate limit exceeded. Resets at ${_formatResetTime(resetTime)}');
      }
      throw Exception('Access forbidden');
    } else {
      throw Exception('Failed to fetch pull requests: ${response.statusCode}');
    }
  }

  Future<List<PullRequestModel>> getCreatedPrs() async {
    final token = await _tokenRepository.getToken();
    final username = await _tokenRepository.getUsername();

    if (token == null || username == null) {
      throw Exception('Not authenticated');
    }

    final query = 'author:$username type:pr state:open';
    final uri = Uri.parse(
      '${AppConstants.githubApiBaseUrl}/search/issues?q=${Uri.encodeComponent(query)}&sort=updated&order=desc&per_page=100',
    );

    final response = await http.get(
      uri,
      headers: {
        'Authorization': 'Bearer $token',
        'Accept': 'application/vnd.github+json',
        'X-GitHub-Api-Version': '2022-11-28',
      },
    ).timeout(const Duration(seconds: 30));

    if (response.statusCode == 200) {
      final data = json.decode(response.body) as Map<String, dynamic>;
      final items = data['items'] as List<dynamic>? ?? [];

      return items
          .map((item) =>
              PullRequestModel.fromGitHubIssue(item as Map<String, dynamic>))
          .toList();
    } else if (response.statusCode == 401) {
      throw Exception('Authentication failed. Please check your token.');
    } else if (response.statusCode == 403) {
      final remaining = response.headers['x-ratelimit-remaining'];
      if (remaining == '0') {
        final resetTime = response.headers['x-ratelimit-reset'];
        throw Exception(
            'Rate limit exceeded. Resets at ${_formatResetTime(resetTime)}');
      }
      throw Exception('Access forbidden');
    } else {
      throw Exception('Failed to fetch pull requests: ${response.statusCode}');
    }
  }

  /// Fetches PRs that the user has reviewed (last 5)
  Future<List<ReviewedPullRequestModel>> getReviewedPrs() async {
    final token = await _tokenRepository.getToken();
    final username = await _tokenRepository.getUsername();

    if (token == null || username == null) {
      throw Exception('Not authenticated');
    }

    // Search for PRs where the user has submitted a review (reviewed-by)
    // Include all states (open, closed, merged)
    final query = 'type:pr reviewed-by:$username -author:$username';
    final uri = Uri.parse(
      '${AppConstants.githubApiBaseUrl}/search/issues?q=${Uri.encodeComponent(query)}&sort=updated&order=desc&per_page=5',
    );

    final response = await http.get(
      uri,
      headers: {
        'Authorization': 'Bearer $token',
        'Accept': 'application/vnd.github+json',
        'X-GitHub-Api-Version': '2022-11-28',
      },
    ).timeout(const Duration(seconds: 30));

    if (response.statusCode == 200) {
      final data = json.decode(response.body) as Map<String, dynamic>;
      final items = data['items'] as List<dynamic>? ?? [];

      // Fetch review info for all PRs in parallel
      final futures = items.map((item) {
        final prData = item as Map<String, dynamic>;
        return _getReviewInfo(token, username, prData);
      }).toList();

      final results = await Future.wait(futures);
      final reviewedPrs = results.whereType<ReviewedPullRequestModel>().toList();

      return reviewedPrs;
    } else if (response.statusCode == 401) {
      throw Exception('Authentication failed. Please check your token.');
    } else if (response.statusCode == 403) {
      final remaining = response.headers['x-ratelimit-remaining'];
      if (remaining == '0') {
        final resetTime = response.headers['x-ratelimit-reset'];
        throw Exception(
            'Rate limit exceeded. Resets at ${_formatResetTime(resetTime)}');
      }
      throw Exception('Access forbidden');
    } else {
      throw Exception('Failed to fetch reviewed PRs: ${response.statusCode}');
    }
  }

  Future<ReviewedPullRequestModel?> _getReviewInfo(
    String token,
    String username,
    Map<String, dynamic> prData,
  ) async {
    try {
      // Extract owner and repo from repository_url
      final repositoryUrl = prData['repository_url'] as String? ?? '';
      final repoParts = repositoryUrl.split('/');
      if (repoParts.length < 2) return null;

      final owner = repoParts[repoParts.length - 2];
      final repo = repoParts.last;
      final prNumber = prData['number'] as int;

      // Fetch reviews for this PR
      final reviewsUri = Uri.parse(
        '${AppConstants.githubApiBaseUrl}/repos/$owner/$repo/pulls/$prNumber/reviews',
      );

      final reviewsResponse = await http.get(
        reviewsUri,
        headers: {
          'Authorization': 'Bearer $token',
          'Accept': 'application/vnd.github+json',
          'X-GitHub-Api-Version': '2022-11-28',
        },
      ).timeout(const Duration(seconds: 10));

      if (reviewsResponse.statusCode != 200) {
        // If we can't get reviews, still show the PR with pending state
        return ReviewedPullRequestModel.fromGitHubIssue(
          prData,
          reviewState: ReviewState.pending,
          reviewedAt: DateTime.parse(prData['updated_at'] as String),
        );
      }

      final reviews = json.decode(reviewsResponse.body) as List<dynamic>;

      // Find the user's most recent review
      ReviewState reviewState = ReviewState.pending;
      DateTime reviewedAt = DateTime.parse(prData['updated_at'] as String);

      for (final review in reviews.reversed) {
        final reviewData = review as Map<String, dynamic>;
        final reviewer = reviewData['user'] as Map<String, dynamic>?;
        if (reviewer?['login'] == username) {
          final state = reviewData['state'] as String? ?? '';
          reviewedAt = DateTime.parse(reviewData['submitted_at'] as String);

          switch (state.toUpperCase()) {
            case 'APPROVED':
              reviewState = ReviewState.approved;
              break;
            case 'CHANGES_REQUESTED':
              reviewState = ReviewState.changesRequested;
              break;
            case 'COMMENTED':
              reviewState = ReviewState.commented;
              break;
            default:
              reviewState = ReviewState.pending;
          }
          break;
        }
      }

      return ReviewedPullRequestModel.fromGitHubIssue(
        prData,
        reviewState: reviewState,
        reviewedAt: reviewedAt,
      );
    } catch (e) {
      // On error, return null to skip this PR
      return null;
    }
  }

  /// Fetches recently created PRs by the user (last 5, any state)
  Future<List<CreatedPullRequestModel>> getRecentlyCreatedPrs() async {
    final token = await _tokenRepository.getToken();
    final username = await _tokenRepository.getUsername();

    if (token == null || username == null) {
      throw Exception('Not authenticated');
    }

    // Search for PRs authored by the user (any state - open, closed, merged)
    final query = 'type:pr author:$username';
    final uri = Uri.parse(
      '${AppConstants.githubApiBaseUrl}/search/issues?q=${Uri.encodeComponent(query)}&sort=created&order=desc&per_page=5',
    );

    final response = await http.get(
      uri,
      headers: {
        'Authorization': 'Bearer $token',
        'Accept': 'application/vnd.github+json',
        'X-GitHub-Api-Version': '2022-11-28',
      },
    ).timeout(const Duration(seconds: 30));

    if (response.statusCode == 200) {
      final data = json.decode(response.body) as Map<String, dynamic>;
      final items = data['items'] as List<dynamic>? ?? [];

      return items
          .map((item) =>
              CreatedPullRequestModel.fromGitHubIssue(item as Map<String, dynamic>))
          .toList();
    } else if (response.statusCode == 401) {
      throw Exception('Authentication failed. Please check your token.');
    } else if (response.statusCode == 403) {
      final remaining = response.headers['x-ratelimit-remaining'];
      if (remaining == '0') {
        final resetTime = response.headers['x-ratelimit-reset'];
        throw Exception(
            'Rate limit exceeded. Resets at ${_formatResetTime(resetTime)}');
      }
      throw Exception('Access forbidden');
    } else {
      throw Exception('Failed to fetch recently created PRs: ${response.statusCode}');
    }
  }

  String _formatResetTime(String? resetTimestamp) {
    if (resetTimestamp == null) return 'unknown';
    final timestamp = int.tryParse(resetTimestamp);
    if (timestamp == null) return 'unknown';
    final resetDate = DateTime.fromMillisecondsSinceEpoch(timestamp * 1000);
    return '${resetDate.hour}:${resetDate.minute.toString().padLeft(2, '0')}';
  }
}
