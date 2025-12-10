import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:graphql/client.dart';

import '../../features/auth/data/token_repository.dart';
import '../constants/app_constants.dart';

final graphQLClientProvider = Provider<GraphQLClient>((ref) {
  // This provider is designed to be overridden or we need a way to access the token synchronously
  // or use a FutureProvider.
  // However, for best practices with Riverpod and async headers, we can use an HttpLink/AuthLink combination.
  // Since the token repository is async, we might want to return a client that always fetches the latest token
  // OR make this a FutureProvider.
  // BUT GraphQLClient itself is synchronous to use (mutate/query are async but client instance is sync).
  
  // A common pattern is to just let the repository handle the client creation or use a FutureProvider.
  // Let's use a computed provider that watches the token repository updates if possible, 
  // but TokenRepository doesn't expose a stream of tokens easily without polling or Notification.
  
  // Simpler approach: The Repository will use this service, and this service will extract the token
  // from the TokenRepository on each request if we use a custom Link, OR we can just simple:
  
  throw UnimplementedError('This provider must be overridden with an authenticated client');
});

// A service that provides the configured client
final graphQLServiceProvider = Provider<GraphQLService>((ref) {
  final tokenRepository = ref.watch(tokenRepositoryProvider);
  return GraphQLService(tokenRepository);
});

class GraphQLService {
  final TokenRepository _tokenRepository;
  GraphQLClient? _client;

  GraphQLService(this._tokenRepository);

  Future<GraphQLClient> get client async {
    if (_client != null) return _client!;
    
    final token = await _tokenRepository.getToken();
    if (token == null) {
      throw Exception('Not authenticated');
    }

    final httpLink = HttpLink(
      '${AppConstants.githubApiBaseUrl}/graphql',
    );

    final authLink = AuthLink(
      getToken: () async => 'Bearer $token',
    );

    final link = authLink.concat(httpLink);

    _client = GraphQLClient(
      link: link,
      // We'll add persistent caching in Phase 2. For now, in-memory.
      cache: GraphQLCache(),
    );

    return _client!;
  }
  
  // Method to reset client (e.g. on logout)
  void reset() {
    _client = null;
  }
}
