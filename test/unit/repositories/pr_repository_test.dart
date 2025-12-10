import 'package:flutter_test/flutter_test.dart';
import 'package:graphql/client.dart';
import 'package:mocktail/mocktail.dart';
import 'package:gitdesk/core/services/graphql_service.dart';
import 'package:gitdesk/core/services/local_storage_service.dart';
import 'package:gitdesk/features/auth/data/token_repository.dart';
import 'package:gitdesk/features/pull_requests/data/pr_repository.dart';

class MockGraphQLService extends Mock implements GraphQLService {}
class MockTokenRepository extends Mock implements TokenRepository {}
class MockLocalStorageService extends Mock implements LocalStorageService {}
class MockGraphQLClient extends Mock implements GraphQLClient {}
class FakeQueryOptions extends Fake implements QueryOptions {}

void main() {
  late PrRepository repository;
  late MockGraphQLService mockGraphQLService;
  late MockTokenRepository mockTokenRepository;
  late MockLocalStorageService mockLocalStorageService;
  late MockGraphQLClient mockGraphQLClient;

  setUpAll(() {
    registerFallbackValue(FakeQueryOptions());
  });

  setUp(() {
    mockGraphQLService = MockGraphQLService();
    mockTokenRepository = MockTokenRepository();
    mockLocalStorageService = MockLocalStorageService();
    mockGraphQLClient = MockGraphQLClient();

    when(() => mockGraphQLService.client).thenAnswer((_) async => mockGraphQLClient);
    when(() => mockTokenRepository.getUsername()).thenAnswer((_) async => 'testuser');
    when(() => mockLocalStorageService.cachePrData(any(), any())).thenAnswer((_) async {});

    repository = PrRepository(
      mockGraphQLService,
      mockTokenRepository,
      mockLocalStorageService,
    );
  });

  group('PrRepository', () {
    test('getReviewRequests fetches data and caches it', () async {
      // API Response Mock
      final mockResult = QueryResult(
        options: QueryOptions(document: gql('')),
        source: QueryResultSource.network,
        data: {
          'search': {
            'pageInfo': {
               'hasNextPage': false,
               'endCursor': null
            },
            'nodes': [
              {
                'databaseId': 123,
                'number': 1,
                'title': 'Test PR',
                'bodyText': 'Body',
                'state': 'OPEN',
                'url': 'https://github.com/owner/repo/pull/1',
                'createdAt': '2023-01-01T00:00:00Z',
                'updatedAt': '2023-01-02T00:00:00Z',
                'isDraft': false,
                'author': {
                  'login': 'author',
                  'avatarUrl': 'url',
                  'url': 'url'
                },
                'repository': {
                  'name': 'repo',
                  'owner': {'login': 'owner'},
                  'url': 'url'
                },
                'labels': {'nodes': []}
              }
            ]
          }
        },
      );

      when(() => mockGraphQLClient.query(any())).thenAnswer((_) async => mockResult);

      final result = await repository.getReviewRequests();

      expect(result.items.length, 1);
      expect(result.items.first.title, 'Test PR');
      
      // Verify caching is called
      verify(() => mockLocalStorageService.cachePrData('review_requests', any())).called(1);
    });

    test('getCachedReviewRequests returns cached data', () async {
      final cachedData = {
        'data': [
          {
            'id': 123,
            'number': 1,
            'title': 'Cached PR',
            'body': 'Body',
            'state': 'open',
            'html_url': 'url',
            'created_at': '2023-01-01T00:00:00Z',
            'updated_at': '2023-01-02T00:00:00Z',
            'draft': false,
            'author': {
              'id': 1,
              'login': 'author',
              'avatar_url': 'url',
              'html_url': 'url'
            },
            'repository': {
              'full_name': 'owner/repo',
              'html_url': 'url'
            },
            'labels': []
          }
        ],
        'timestamp': '2023-01-01'
      };

      when(() => mockLocalStorageService.getCachedPrData('review_requests'))
          .thenAnswer((_) async => cachedData);

      final result = await repository.getCachedReviewRequests();

      expect(result.items.length, 1);
      expect(result.items.first.title, 'Cached PR');
    });
  });
}
