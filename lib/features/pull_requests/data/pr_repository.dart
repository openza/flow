import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:graphql/client.dart';

import '../../../core/models/paginated_result.dart';
import '../../../core/services/graphql_service.dart';
import '../../../core/services/local_storage_service.dart';
import '../../auth/data/token_repository.dart';
import '../domain/models/pull_request.dart';

final prRepositoryProvider = Provider<PrRepository>((ref) {
  final graphQLService = ref.watch(graphQLServiceProvider);
  final tokenRepo = ref.watch(tokenRepositoryProvider);
  final localStorage = ref.watch(localStorageServiceProvider);
  return PrRepository(graphQLService, tokenRepo, localStorage);
});

class PrRepository {
  final GraphQLService _graphQLService;
  final TokenRepository _tokenRepository;
  final LocalStorageService _localStorage;

  PrRepository(
    this._graphQLService,
    this._tokenRepository,
    this._localStorage,
  );

  static const _reviewRequestsKey = 'review_requests';
  static const _createdPrsKey = 'created_prs';
  static const _reviewedPrsKey = 'reviewed_prs';
  static const _recentlyCreatedPrsKey = 'recently_created_prs';

  Future<PaginatedResult<PullRequestModel>> getReviewRequests({String? afterCursor}) async {
    final username = await _tokenRepository.getUsername();
    if (username == null) throw Exception('Not authenticated');

    final query = 'type:pr state:open review-requested:$username sort:updated-desc';
    final result = await _searchPullRequests(query, afterCursor: afterCursor);

    // Cache ONLY the first page
    if (afterCursor == null) {
      await _localStorage.cachePrData(_reviewRequestsKey, {
        'data': result.items.map((e) => e.toJson()).toList(),
        'timestamp': DateTime.now().toIso8601String(),
      });
    }

    return result;
  }

  Future<PaginatedResult<PullRequestModel>> getCachedReviewRequests() async {
    final data = await _localStorage.getCachedPrData(_reviewRequestsKey);
    if (data == null) return PaginatedResult.empty();

    final list = data['data'] as List<dynamic>? ?? [];
    final items = list.map((e) => PullRequestModel.fromJson(e)).toList();
    
    // We treat cached data as a single page with no next page for simplicity
    return PaginatedResult(items: items, hasNextPage: false);
  }

  Future<PaginatedResult<PullRequestModel>> getCreatedPrs({String? afterCursor}) async {
    final username = await _tokenRepository.getUsername();
    if (username == null) throw Exception('Not authenticated');

    final query = 'author:$username type:pr state:open sort:updated-desc';
    final result = await _searchPullRequests(query, afterCursor: afterCursor);

    if (afterCursor == null) {
      await _localStorage.cachePrData(_createdPrsKey, {
        'data': result.items.map((e) => e.toJson()).toList(),
        'timestamp': DateTime.now().toIso8601String(),
      });
    }

    return result;
  }

  Future<PaginatedResult<PullRequestModel>> getCachedCreatedPrs() async {
    final data = await _localStorage.getCachedPrData(_createdPrsKey);
    if (data == null) return PaginatedResult.empty();

    final list = data['data'] as List<dynamic>? ?? [];
    final items = list.map((e) => PullRequestModel.fromJson(e)).toList();
    return PaginatedResult(items: items, hasNextPage: false);
  }

  Future<PaginatedResult<ReviewedPullRequestModel>> getReviewedPrs({String? afterCursor}) async {
    final username = await _tokenRepository.getUsername();
    if (username == null) throw Exception('Not authenticated');

    final query = 'type:pr reviewed-by:$username -author:$username sort:updated-desc';
    
    final client = await _graphQLService.client;
    
    const String graphqlQuery = r'''
      query SearchReviewedPRs($query: String!, $cursor: String) {
        search(query: $query, type: ISSUE, first: 20, after: $cursor) {
          pageInfo {
            hasNextPage
            endCursor
          }
          nodes {
            ... on PullRequest {
              databaseId
              number
              title
              url
              state
              mergedAt
              updatedAt
              baseRefName
              headRefName
              author {
                login
                avatarUrl
                url
              }
              repository {
                name
                owner {
                  login
                }
                url
              }
              reviews(last: 20) {
                nodes {
                  state
                  author {
                    login
                  }
                  submittedAt
                }
              }
            }
          }
        }
      }
    ''';

    final result = await client.query(QueryOptions(
      document: gql(graphqlQuery),
      variables: {
        'query': query,
        'cursor': afterCursor,
      },
      fetchPolicy: FetchPolicy.networkOnly,
    ));

    if (result.hasException) {
      throw Exception(result.exception.toString());
    }

    final List<ReviewedPullRequestModel> reviewedPrs = [];
    final search = result.data?['search'];
    final nodes = search?['nodes'] as List<dynamic>? ?? [];
    final pageInfo = search?['pageInfo'];

    for (final node in nodes) {
      if (node == null) continue;
      final pr = _mapToReviewedPrModel(node, username);
      if (pr != null) {
        reviewedPrs.add(pr);
      }
    }

    final paginatedResult = PaginatedResult(
      items: reviewedPrs,
      hasNextPage: pageInfo?['hasNextPage'] as bool? ?? false,
      endCursor: pageInfo?['endCursor'] as String?,
    );

    if (afterCursor == null) {
      await _localStorage.cachePrData(_reviewedPrsKey, {
        'data': reviewedPrs.map((e) => e.toJson()).toList(),
        'timestamp': DateTime.now().toIso8601String(),
      });
    }

    return paginatedResult;
  }

  Future<PaginatedResult<ReviewedPullRequestModel>> getCachedReviewedPrs() async {
    final data = await _localStorage.getCachedPrData(_reviewedPrsKey);
    if (data == null) return PaginatedResult.empty();

    final list = data['data'] as List<dynamic>? ?? [];
    final items = list.map((e) => ReviewedPullRequestModel.fromJson(e)).toList();
    return PaginatedResult(items: items, hasNextPage: false);
  }

  Future<List<CreatedPullRequestModel>> getRecentlyCreatedPrs() async {
    final username = await _tokenRepository.getUsername();
    if (username == null) throw Exception('Not authenticated');

    final query = 'type:pr author:$username sort:created-desc';
    
    final client = await _graphQLService.client;
    
    // We keep this one simple (top 5), no pagination needed for "Recently Created" usually
    const String graphqlQuery = r'''
      query SearchCreatedPRs($query: String!) {
        search(query: $query, type: ISSUE, first: 5) {
          nodes {
            ... on PullRequest {
              databaseId
              number
              title
              url
              state
              mergedAt
              createdAt
              baseRefName
              headRefName
              repository {
                name
                owner {
                  login
                }
                url
              }
            }
          }
        }
      }
    ''';

    final result = await client.query(QueryOptions(
      document: gql(graphqlQuery),
      variables: {'query': query},
      fetchPolicy: FetchPolicy.networkOnly,
    ));

    if (result.hasException) {
      throw Exception(result.exception.toString());
    }

    final List<CreatedPullRequestModel> createdPrs = [];
    final nodes = result.data?['search']['nodes'] as List<dynamic>? ?? [];

    for (final node in nodes) {
      if (node == null) continue;
      createdPrs.add(_mapToCreatedPrModel(node));
    }

    await _localStorage.cachePrData(_recentlyCreatedPrsKey, {
      'data': createdPrs.map((e) => e.toJson()).toList(),
      'timestamp': DateTime.now().toIso8601String(),
    });

    return createdPrs;
  }

  Future<List<CreatedPullRequestModel>> getCachedRecentlyCreatedPrs() async {
    final data = await _localStorage.getCachedPrData(_recentlyCreatedPrsKey);
    if (data == null) return [];

    final list = data['data'] as List<dynamic>? ?? [];
    return list.map((e) => CreatedPullRequestModel.fromJson(e)).toList();
  }

  Future<PaginatedResult<PullRequestModel>> _searchPullRequests(
    String searchQuery, {
    String? afterCursor,
  }) async {
    final client = await _graphQLService.client;

    const String graphqlQuery = r'''
      query SearchPRs($query: String!, $cursor: String) {
        search(query: $query, type: ISSUE, first: 20, after: $cursor) {
          pageInfo {
            hasNextPage
            endCursor
          }
          nodes {
            ... on PullRequest {
              databaseId
              number
              title
              bodyText
              state
              url
              createdAt
              updatedAt
              isDraft
              baseRefName
              headRefName
              author {
                login
                avatarUrl
                url
              }
              repository {
                name
                owner {
                  login
                }
                url
              }
              labels(first: 10) {
                nodes {
                  name
                  color
                  description
                }
              }
            }
          }
        }
      }
    ''';

    final result = await client.query(QueryOptions(
      document: gql(graphqlQuery),
      variables: {
        'query': searchQuery,
        'cursor': afterCursor,
      },
      fetchPolicy: FetchPolicy.networkOnly,
    ));

    if (result.hasException) {
      throw Exception(result.exception.toString());
    }

    final List<PullRequestModel> prs = [];
    final search = result.data?['search'];
    final nodes = search?['nodes'] as List<dynamic>? ?? [];
    final pageInfo = search?['pageInfo'];

    for (final node in nodes) {
      if (node == null) continue;
      prs.add(_mapToPrModel(node));
    }

    return PaginatedResult(
      items: prs,
      hasNextPage: pageInfo?['hasNextPage'] as bool? ?? false,
      endCursor: pageInfo?['endCursor'] as String?,
    );
  }
  
  // Public method for arbitrary search (server-side)
  Future<PaginatedResult<PullRequestModel>> searchPullRequests(
    String query, {
    String? afterCursor,
  }) async {
    // Add type:pr if not present, though user might want to search issues too? 
    // For this app (GitDesk) it's PR focused.
    final fullQuery = query.contains('type:pr') ? query : '$query type:pr';
    return _searchPullRequests(fullQuery, afterCursor: afterCursor);
  }

  // Mappers
  
  PullRequestModel _mapToPrModel(Map<String, dynamic> node) {
    final author = node['author'] ?? {};
    final repository = node['repository'] ?? {};
    final labels = node['labels']?['nodes'] as List<dynamic>? ?? [];

    return PullRequestModel(
      id: node['databaseId'] as int,
      number: node['number'] as int,
      title: node['title'] as String,
      body: node['bodyText'] as String? ?? '',
      state: (node['state'] as String).toLowerCase(),
      htmlUrl: node['url'] as String,
      createdAt: DateTime.parse(node['createdAt'] as String),
      updatedAt: DateTime.parse(node['updatedAt'] as String),
      draft: node['isDraft'] as bool,
      baseRefName: node['baseRefName'] as String? ?? '',
      headRefName: node['headRefName'] as String? ?? '',
      author: UserModel(
        id: 0,
        login: author['login'] as String,
        avatarUrl: author['avatarUrl'] as String,
        htmlUrl: author['url'] as String,
      ),
      repository: RepositoryModel(
        fullName: '${repository['owner']['login']}/${repository['name']}',
        htmlUrl: repository['url'] as String,
      ),
      labels: labels.map((l) => LabelModel(
        id: 0,
        name: l['name'] as String,
        color: l['color'] as String,
        description: l['description'] as String?,
      )).toList(),
    );
  }

  ReviewedPullRequestModel? _mapToReviewedPrModel(Map<String, dynamic> node, String currentUsername) {
    final author = node['author'] ?? {};
    final repository = node['repository'] ?? {};
    final reviews = node['reviews']?['nodes'] as List<dynamic>? ?? [];

    ReviewState reviewState = ReviewState.pending;
    DateTime reviewedAt = DateTime.parse(node['updatedAt'] as String);

    for (final review in reviews.reversed) {
      final reviewerLogin = review['author']?['login'] as String?;
      if (reviewerLogin == currentUsername) {
        final state = review['state'] as String;
        reviewedAt = DateTime.parse(review['submittedAt'] as String);
        
        switch (state) {
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

    final state = (node['state'] as String).toLowerCase();
    final isMerged = node['mergedAt'] != null;
    
    MergeState mergeState;
    if (isMerged) {
      mergeState = MergeState.merged;
    } else if (state == 'closed') {
      mergeState = MergeState.closed;
    } else {
      mergeState = MergeState.open;
    }

    return ReviewedPullRequestModel(
      id: node['databaseId'] as int,
      number: node['number'] as int,
      title: node['title'] as String,
      htmlUrl: node['url'] as String,
      reviewedAt: reviewedAt,
      reviewState: reviewState,
      mergeState: mergeState,
      baseRefName: node['baseRefName'] as String? ?? '',
      headRefName: node['headRefName'] as String? ?? '',
      author: UserModel(
        id: 0,
        login: author['login'] as String,
        avatarUrl: author['avatarUrl'] as String,
        htmlUrl: author['url'] as String,
      ),
      repository: RepositoryModel(
        fullName: '${repository['owner']['login']}/${repository['name']}',
        htmlUrl: repository['url'] as String,
      ),
    );
  }

  CreatedPullRequestModel _mapToCreatedPrModel(Map<String, dynamic> node) {
    final repository = node['repository'] ?? {};
    
    final state = (node['state'] as String).toLowerCase();
    final isMerged = node['mergedAt'] != null;
    
    MergeState mergeState;
    if (isMerged) {
      mergeState = MergeState.merged;
    } else if (state == 'closed') {
      mergeState = MergeState.closed;
    } else {
      mergeState = MergeState.open;
    }

    return CreatedPullRequestModel(
      id: node['databaseId'] as int,
      number: node['number'] as int,
      title: node['title'] as String,
      htmlUrl: node['url'] as String,
      createdAt: DateTime.parse(node['createdAt'] as String),
      mergeState: mergeState,
      baseRefName: node['baseRefName'] as String? ?? '',
      headRefName: node['headRefName'] as String? ?? '',
      repository: RepositoryModel(
        fullName: '${repository['owner']['login']}/${repository['name']}',
        htmlUrl: repository['url'] as String,
      ),
    );
  }
}
