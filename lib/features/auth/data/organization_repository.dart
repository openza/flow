import 'dart:convert';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:graphql/client.dart';

import '../../../core/constants/app_constants.dart';
import '../../../core/services/graphql_service.dart';

final organizationRepositoryProvider = Provider<OrganizationRepository>((ref) {
  final graphQLService = ref.watch(graphQLServiceProvider);
  return OrganizationRepository(graphQLService);
});

class GithubOrganization {
  final String login;
  final String name;
  final String avatarUrl;

  GithubOrganization({
    required this.login,
    required this.name,
    required this.avatarUrl,
  });

  Map<String, dynamic> toJson() => {
        'login': login,
        'name': name,
        'avatarUrl': avatarUrl,
      };

  factory GithubOrganization.fromJson(Map<String, dynamic> json) {
    return GithubOrganization(
      login: json['login'] as String,
      name: json['name'] as String? ?? json['login'] as String,
      avatarUrl: json['avatarUrl'] as String,
    );
  }
}

class OrganizationRepository {
  final GraphQLService _graphQLService;
  final FlutterSecureStorage _storage = const FlutterSecureStorage();

  // In-memory cache
  List<GithubOrganization>? _cachedOrgs;
  String? _cachedSelectedOrg;

  OrganizationRepository(this._graphQLService);

  static const String _orgsQuery = r'''
    query GetUserOrganizations {
      viewer {
        organizations(first: 100) {
          nodes {
            login
            name
            avatarUrl
          }
        }
      }
    }
  ''';

  /// Fetches the user's organizations from GitHub GraphQL API.
  Future<List<GithubOrganization>> fetchOrganizations() async {
    final client = await _graphQLService.client;

    final result = await client.query(QueryOptions(
      document: gql(_orgsQuery),
      fetchPolicy: FetchPolicy.networkOnly,
    ));

    if (result.hasException) {
      throw Exception('Failed to fetch organizations: ${result.exception}');
    }

    final nodes = result.data?['viewer']?['organizations']?['nodes'] as List<dynamic>? ?? [];

    final orgs = nodes
        .where((node) => node != null)
        .map((node) => GithubOrganization.fromJson(node as Map<String, dynamic>))
        .toList();

    // Cache in memory and storage
    _cachedOrgs = orgs;
    await _saveOrgsToStorage(orgs);

    return orgs;
  }

  /// Returns cached organizations (from memory or storage).
  Future<List<GithubOrganization>> getCachedOrganizations() async {
    if (_cachedOrgs != null) {
      return _cachedOrgs!;
    }

    final stored = await _storage.read(key: AppConstants.userOrgsStorageKey);
    if (stored != null) {
      try {
        final list = json.decode(stored) as List<dynamic>;
        _cachedOrgs = list
            .map((e) => GithubOrganization.fromJson(e as Map<String, dynamic>))
            .toList();
        return _cachedOrgs!;
      } catch (e) {
        // Invalid cached data, return empty
        return [];
      }
    }

    return [];
  }

  Future<void> _saveOrgsToStorage(List<GithubOrganization> orgs) async {
    final jsonStr = json.encode(orgs.map((o) => o.toJson()).toList());
    await _storage.write(key: AppConstants.userOrgsStorageKey, value: jsonStr);
  }

  /// Gets the currently selected organization filter.
  /// Returns null if "All Organizations" is selected.
  Future<String?> getSelectedOrganization() async {
    if (_cachedSelectedOrg != null) {
      return _cachedSelectedOrg;
    }

    _cachedSelectedOrg = await _storage.read(key: AppConstants.selectedOrgStorageKey);
    return _cachedSelectedOrg;
  }

  /// Saves the selected organization filter.
  /// Pass null for "All Organizations".
  Future<void> saveSelectedOrganization(String? orgLogin) async {
    _cachedSelectedOrg = orgLogin;
    if (orgLogin == null) {
      await _storage.delete(key: AppConstants.selectedOrgStorageKey);
    } else {
      await _storage.write(key: AppConstants.selectedOrgStorageKey, value: orgLogin);
    }
  }

  /// Clears all cached organization data (called on logout).
  Future<void> clearAll() async {
    _cachedOrgs = null;
    _cachedSelectedOrg = null;
    await _storage.delete(key: AppConstants.userOrgsStorageKey);
    await _storage.delete(key: AppConstants.selectedOrgStorageKey);
  }
}
