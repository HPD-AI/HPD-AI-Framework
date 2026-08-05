namespace HPD.Base;

/// <summary>Defines the capability status contract.</summary>
public enum CapabilityStatus { /// <summary>Identifies available.</summary>
Available, /// <summary>Identifies unavailable.</summary>
Unavailable, /// <summary>Identifies degraded.</summary>
Degraded, /// <summary>Identifies disabled.</summary>
Disabled, /// <summary>Identifies planned.</summary>
Planned }
/// <summary>Defines the capability scope contract.</summary>
public enum CapabilityScope { /// <summary>Identifies runtime.</summary>
Runtime, /// <summary>Identifies collection.</summary>
Collection, /// <summary>Identifies field.</summary>
Field, /// <summary>Identifies store.</summary>
Store, /// <summary>Identifies projection.</summary>
Projection, /// <summary>Identifies admin.</summary>
Admin }
/// <summary>Defines the support level contract.</summary>
public enum SupportLevel { /// <summary>Identifies required.</summary>
Required, /// <summary>Identifies optional.</summary>
Optional, /// <summary>Identifies experimental.</summary>
Experimental, /// <summary>Identifies preview.</summary>
Preview, /// <summary>Identifies deprecated.</summary>
Deprecated }
