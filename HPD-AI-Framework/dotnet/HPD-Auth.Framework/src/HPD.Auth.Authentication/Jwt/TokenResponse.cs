// TokenResponse and UserTokenDto are defined in HPD.Auth.Core.Models to avoid a
// circular project reference (HPD.Auth.Authentication already references
// HPD.Auth.Core; placing the types back in Authentication would require Core to
// reference Authentication, creating a cycle).
//
// Consumers of HPD.Auth.Authentication should import:
//   using HPD.Auth.Core.Models;
//
// The types are:
//   - HPD.Auth.Core.Models.TokenResponse    — -compatible token response
//   - HPD.Auth.Core.Models.UserTokenDto     — Embedded user in the token response
//
// Global namespace aliases are provided below so that code within this assembly
// can reference them without a fully-qualified name.

global using TokenResponse  = HPD.Auth.Core.Models.TokenResponse;
global using UserTokenDto   = HPD.Auth.Core.Models.UserTokenDto;
