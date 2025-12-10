class PaginatedResult<T> {
  final List<T> items;
  final bool hasNextPage;
  final String? endCursor;

  PaginatedResult({
    required this.items,
    required this.hasNextPage,
    this.endCursor,
  });

  factory PaginatedResult.empty() {
    return PaginatedResult(items: [], hasNextPage: false);
  }
}
