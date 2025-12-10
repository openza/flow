import 'package:flutter/material.dart';

class PullRequestModel {
  final int id;
  final int number;
  final String title;
  final String body;
  final String state;
  final String htmlUrl;
  final DateTime createdAt;
  final DateTime updatedAt;
  final bool draft;
  final UserModel author;
  final RepositoryModel repository;
  final List<LabelModel> labels;

  PullRequestModel({
    required this.id,
    required this.number,
    required this.title,
    required this.body,
    required this.state,
    required this.htmlUrl,
    required this.createdAt,
    required this.updatedAt,
    required this.draft,
    required this.author,
    required this.repository,
    required this.labels,
  });

  factory PullRequestModel.fromGitHubIssue(Map<String, dynamic> json) {
    final repositoryUrl = json['repository_url'] as String? ?? '';
    final repoParts = repositoryUrl.split('/');
    final repoName = repoParts.length >= 2
        ? '${repoParts[repoParts.length - 2]}/${repoParts.last}'
        : '';

    return PullRequestModel(
      id: json['id'] as int,
      number: json['number'] as int,
      title: json['title'] as String? ?? '',
      body: json['body'] as String? ?? '',
      state: json['state'] as String? ?? 'open',
      htmlUrl: json['html_url'] as String? ?? '',
      createdAt: DateTime.parse(json['created_at'] as String),
      updatedAt: DateTime.parse(json['updated_at'] as String),
      draft: json['draft'] as bool? ?? false,
      author: UserModel.fromJson(json['user'] as Map<String, dynamic>? ?? {}),
      repository: RepositoryModel(
        fullName: repoName,
        htmlUrl: json['repository_url'] as String? ?? '',
      ),
      labels: (json['labels'] as List<dynamic>? ?? [])
          .map((l) => LabelModel.fromJson(l as Map<String, dynamic>))
          .toList(),
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'id': id,
      'number': number,
      'title': title,
      'body': body,
      'state': state,
      'html_url': htmlUrl,
      'created_at': createdAt.toIso8601String(),
      'updated_at': updatedAt.toIso8601String(),
      'draft': draft,
      'author': author.toJson(),
      'repository': repository.toJson(),
      'labels': labels.map((l) => l.toJson()).toList(),
    };
  }

  factory PullRequestModel.fromJson(Map<String, dynamic> json) {
    return PullRequestModel(
      id: json['id'] as int,
      number: json['number'] as int,
      title: json['title'] as String,
      body: json['body'] as String,
      state: json['state'] as String,
      htmlUrl: json['html_url'] as String,
      createdAt: DateTime.parse(json['created_at'] as String),
      updatedAt: DateTime.parse(json['updated_at'] as String),
      draft: json['draft'] as bool,
      author: UserModel.fromJson(json['author'] as Map<String, dynamic>),
      repository:
          RepositoryModel.fromJson(json['repository'] as Map<String, dynamic>),
      labels: (json['labels'] as List<dynamic>)
          .map((l) => LabelModel.fromJson(l as Map<String, dynamic>))
          .toList(),
    );
  }
}

class UserModel {
  final int id;
  final String login;
  final String avatarUrl;
  final String htmlUrl;

  UserModel({
    required this.id,
    required this.login,
    required this.avatarUrl,
    required this.htmlUrl,
  });

  factory UserModel.fromJson(Map<String, dynamic> json) {
    return UserModel(
      id: json['id'] as int? ?? 0,
      login: json['login'] as String? ?? '',
      avatarUrl: json['avatar_url'] as String? ?? '',
      htmlUrl: json['html_url'] as String? ?? '',
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'id': id,
      'login': login,
      'avatar_url': avatarUrl,
      'html_url': htmlUrl,
    };
  }
}

class RepositoryModel {
  final String fullName;
  final String htmlUrl;

  RepositoryModel({
    required this.fullName,
    required this.htmlUrl,
  });

  String get owner => fullName.split('/').firstOrNull ?? '';
  String get name => fullName.split('/').lastOrNull ?? '';

  factory RepositoryModel.fromJson(Map<String, dynamic> json) {
    return RepositoryModel(
      fullName: json['full_name'] as String,
      htmlUrl: json['html_url'] as String,
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'full_name': fullName,
      'html_url': htmlUrl,
    };
  }
}

enum ReviewState {
  approved,
  changesRequested,
  commented,
  pending,
}

enum MergeState {
  merged,
  open,
  closed,
}

class ReviewedPullRequestModel {
  final int id;
  final int number;
  final String title;
  final String htmlUrl;
  final DateTime reviewedAt;
  final ReviewState reviewState;
  final MergeState mergeState;
  final UserModel author;
  final RepositoryModel repository;

  ReviewedPullRequestModel({
    required this.id,
    required this.number,
    required this.title,
    required this.htmlUrl,
    required this.reviewedAt,
    required this.reviewState,
    required this.mergeState,
    required this.author,
    required this.repository,
  });

  factory ReviewedPullRequestModel.fromGitHubIssue(
    Map<String, dynamic> json, {
    required ReviewState reviewState,
    required DateTime reviewedAt,
  }) {
    // Legacy support for REST
    // ... same as before but adapted if needed
    // Simplified since we are moving away from REST
    final repositoryUrl = json['repository_url'] as String? ?? '';
    final repoParts = repositoryUrl.split('/');
    final repoName = repoParts.length >= 2
        ? '${repoParts[repoParts.length - 2]}/${repoParts.last}'
        : '';

    final state = json['state'] as String? ?? 'open';
    final pullRequest = json['pull_request'] as Map<String, dynamic>?;
    final isMerged = pullRequest?['merged_at'] != null;

    MergeState mergeState;
    if (isMerged) {
      mergeState = MergeState.merged;
    } else if (state == 'closed') {
      mergeState = MergeState.closed;
    } else {
      mergeState = MergeState.open;
    }

    return ReviewedPullRequestModel(
      id: json['id'] as int,
      number: json['number'] as int,
      title: json['title'] as String? ?? '',
      htmlUrl: json['html_url'] as String? ?? '',
      reviewedAt: reviewedAt,
      reviewState: reviewState,
      mergeState: mergeState,
      author: UserModel.fromJson(json['user'] as Map<String, dynamic>? ?? {}),
      repository: RepositoryModel(
        fullName: repoName,
        htmlUrl: repositoryUrl,
      ),
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'id': id,
      'number': number,
      'title': title,
      'html_url': htmlUrl,
      'reviewed_at': reviewedAt.toIso8601String(),
      'review_state': reviewState.index,
      'merge_state': mergeState.index,
      'author': author.toJson(),
      'repository': repository.toJson(),
    };
  }

  factory ReviewedPullRequestModel.fromJson(Map<String, dynamic> json) {
    return ReviewedPullRequestModel(
      id: json['id'] as int,
      number: json['number'] as int,
      title: json['title'] as String,
      htmlUrl: json['html_url'] as String,
      reviewedAt: DateTime.parse(json['reviewed_at'] as String),
      reviewState: ReviewState.values[json['review_state'] as int],
      mergeState: MergeState.values[json['merge_state'] as int],
      author: UserModel.fromJson(json['author'] as Map<String, dynamic>),
      repository:
          RepositoryModel.fromJson(json['repository'] as Map<String, dynamic>),
    );
  }
}

class CreatedPullRequestModel {
  final int id;
  final int number;
  final String title;
  final String htmlUrl;
  final DateTime createdAt;
  final MergeState mergeState;
  final RepositoryModel repository;

  CreatedPullRequestModel({
    required this.id,
    required this.number,
    required this.title,
    required this.htmlUrl,
    required this.createdAt,
    required this.mergeState,
    required this.repository,
  });

  factory CreatedPullRequestModel.fromGitHubIssue(Map<String, dynamic> json) {
    final repositoryUrl = json['repository_url'] as String? ?? '';
    final repoParts = repositoryUrl.split('/');
    final repoName = repoParts.length >= 2
        ? '${repoParts[repoParts.length - 2]}/${repoParts.last}'
        : '';

    final state = json['state'] as String? ?? 'open';
    final pullRequest = json['pull_request'] as Map<String, dynamic>?;
    final isMerged = pullRequest?['merged_at'] != null;

    MergeState mergeState;
    if (isMerged) {
      mergeState = MergeState.merged;
    } else if (state == 'closed') {
      mergeState = MergeState.closed;
    } else {
      mergeState = MergeState.open;
    }

    return CreatedPullRequestModel(
      id: json['id'] as int,
      number: json['number'] as int,
      title: json['title'] as String? ?? '',
      htmlUrl: json['html_url'] as String? ?? '',
      createdAt: DateTime.parse(json['created_at'] as String),
      mergeState: mergeState,
      repository: RepositoryModel(
        fullName: repoName,
        htmlUrl: repositoryUrl,
      ),
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'id': id,
      'number': number,
      'title': title,
      'html_url': htmlUrl,
      'created_at': createdAt.toIso8601String(),
      'merge_state': mergeState.index,
      'repository': repository.toJson(),
    };
  }

  factory CreatedPullRequestModel.fromJson(Map<String, dynamic> json) {
    return CreatedPullRequestModel(
      id: json['id'] as int,
      number: json['number'] as int,
      title: json['title'] as String,
      htmlUrl: json['html_url'] as String,
      createdAt: DateTime.parse(json['created_at'] as String),
      mergeState: MergeState.values[json['merge_state'] as int],
      repository:
          RepositoryModel.fromJson(json['repository'] as Map<String, dynamic>),
    );
  }
}

class LabelModel {
  final int id;
  final String name;
  final String color;
  final String? description;

  LabelModel({
    required this.id,
    required this.name,
    required this.color,
    this.description,
  });

  factory LabelModel.fromJson(Map<String, dynamic> json) {
    return LabelModel(
      id: json['id'] as int? ?? 0,
      name: json['name'] as String? ?? '',
      color: json['color'] as String? ?? '000000',
      description: json['description'] as String?,
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'id': id,
      'name': name,
      'color': color,
      'description': description,
    };
  }

  Color get backgroundColor {
    final colorInt = int.tryParse(color, radix: 16) ?? 0;
    return Color(0xFF000000 | colorInt);
  }

  Color get textColor {
    final bg = backgroundColor;
    final r = (bg.r * 255.0).round().clamp(0, 255);
    final g = (bg.g * 255.0).round().clamp(0, 255);
    final b = (bg.b * 255.0).round().clamp(0, 255);
    final luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255;
    return luminance > 0.5 ? Colors.black : Colors.white;
  }
}
