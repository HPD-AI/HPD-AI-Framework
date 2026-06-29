namespace HPD.Base.Query;

public enum FilterNodeKind { True, False, Not, And, Or, Compare, In, Between, IsNull, IsDefined, Extension }
public enum FilterOperator { Equal, NotEqual, LessThan, LessThanOrEqual, GreaterThan, GreaterThanOrEqual, Contains, NotContains, StartsWith, EndsWith, Like, NotLike }
public enum QueryValueKind { Null, String, Boolean, Integer, Number, Decimal, DateTime, Id, Array }
public enum QuerySortDirection { Asc, Desc }
public enum QueryNullOrder { Unspecified, First, Last }
public enum QueryPaginationMode { Page, Offset, Cursor }
public enum QueryCursorDirection { After, Before }
public enum QueryCountMode { None, IfAvailable, Exact, Estimated, Limited }
public enum QueryOperatorPlacement { FilterExpression, RecordQueryExtension, IncludeExtension }
public enum FilterUsage { ExternalQuery, PolicyConstraint, PolicyWriteCheck, GrantCondition, IncludeFilter, StorePushdown, InMemoryPostFilter }
public enum QueryExecutionMode { Native, PostFetch, Mixed, Unsupported }
