namespace HPD.Base;

/// <summary>Defines the filter node kind contract.</summary>
public enum FilterNodeKind { /// <summary>Identifies true.</summary>
True, /// <summary>Identifies false.</summary>
False, /// <summary>Identifies not.</summary>
Not, /// <summary>Identifies and.</summary>
And, /// <summary>Identifies or.</summary>
Or, /// <summary>Identifies compare.</summary>
Compare, /// <summary>Identifies in.</summary>
In, /// <summary>Identifies between.</summary>
Between, /// <summary>Identifies is null.</summary>
IsNull, /// <summary>Identifies is defined.</summary>
IsDefined, /// <summary>Identifies extension.</summary>
Extension }
/// <summary>Defines the filter operator contract.</summary>
public enum FilterOperator { /// <summary>Identifies equal.</summary>
Equal, /// <summary>Identifies not equal.</summary>
NotEqual, /// <summary>Identifies less than.</summary>
LessThan, /// <summary>Identifies less than or equal.</summary>
LessThanOrEqual, /// <summary>Identifies greater than.</summary>
GreaterThan, /// <summary>Identifies greater than or equal.</summary>
GreaterThanOrEqual, /// <summary>Identifies contains.</summary>
Contains, /// <summary>Identifies not contains.</summary>
NotContains, /// <summary>Identifies starts with.</summary>
StartsWith, /// <summary>Identifies ends with.</summary>
EndsWith, /// <summary>Identifies like.</summary>
Like, /// <summary>Identifies not like.</summary>
NotLike }
/// <summary>Defines the query value kind contract.</summary>
public enum QueryValueKind { /// <summary>Identifies null.</summary>
Null, /// <summary>Identifies string.</summary>
String, /// <summary>Identifies boolean.</summary>
Boolean, /// <summary>Identifies integer.</summary>
Integer, /// <summary>Identifies number.</summary>
Number, /// <summary>Identifies decimal.</summary>
Decimal, /// <summary>Identifies date time.</summary>
DateTime, /// <summary>Identifies ID.</summary>
Id, /// <summary>Identifies array.</summary>
Array }
/// <summary>Defines the query sort direction contract.</summary>
public enum QuerySortDirection { /// <summary>Identifies asc.</summary>
Asc, /// <summary>Identifies desc.</summary>
Desc }
/// <summary>Defines the query null order contract.</summary>
public enum QueryNullOrder { /// <summary>Identifies unspecified.</summary>
Unspecified, /// <summary>Identifies first.</summary>
First, /// <summary>Identifies last.</summary>
Last }
/// <summary>Defines the query pagination mode contract.</summary>
public enum QueryPaginationMode { /// <summary>Identifies page.</summary>
Page, /// <summary>Identifies offset.</summary>
Offset, /// <summary>Identifies cursor.</summary>
Cursor }
/// <summary>Defines the query cursor direction contract.</summary>
public enum QueryCursorDirection { /// <summary>Identifies after.</summary>
After, /// <summary>Identifies before.</summary>
Before }
/// <summary>Defines the query count mode contract.</summary>
public enum QueryCountMode { /// <summary>Identifies none.</summary>
None, /// <summary>Identifies if available.</summary>
IfAvailable, /// <summary>Identifies exact.</summary>
Exact, /// <summary>Identifies estimated.</summary>
Estimated, /// <summary>Identifies limited.</summary>
Limited }
/// <summary>Defines the query operator placement contract.</summary>
public enum QueryOperatorPlacement { /// <summary>Identifies filter expression.</summary>
FilterExpression, /// <summary>Identifies record query extension.</summary>
RecordQueryExtension, /// <summary>Identifies include extension.</summary>
IncludeExtension }
/// <summary>Defines the filter usage contract.</summary>
public enum FilterUsage { /// <summary>Identifies external query.</summary>
ExternalQuery, /// <summary>Identifies policy constraint.</summary>
PolicyConstraint, /// <summary>Identifies policy write check.</summary>
PolicyWriteCheck, /// <summary>Identifies grant condition.</summary>
GrantCondition, /// <summary>Identifies include filter.</summary>
IncludeFilter, /// <summary>Identifies store pushdown.</summary>
StorePushdown, /// <summary>Identifies in memory post filter.</summary>
InMemoryPostFilter }
/// <summary>Defines the query execution mode contract.</summary>
public enum QueryExecutionMode { /// <summary>Identifies native.</summary>
Native, /// <summary>Identifies post fetch.</summary>
PostFetch, /// <summary>Identifies mixed.</summary>
Mixed, /// <summary>Identifies unsupported.</summary>
Unsupported }
