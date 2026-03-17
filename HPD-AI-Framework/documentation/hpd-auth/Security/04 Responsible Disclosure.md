# Responsible Disclosure

If you discover a security vulnerability in HPD.Auth, please report it responsibly rather than publicly disclosing it before a fix is available.

## How to report

Email: **security@hpd.ai**

Include:
- A description of the vulnerability
- Steps to reproduce
- The potential impact
- Any proof-of-concept code (if applicable)

## What to expect

- Acknowledgement within 48 hours
- An assessment of severity and impact within 7 days
- A fix and coordinated disclosure timeline

## Scope

In scope:
- Authentication bypass
- Token forgery or privilege escalation
- Session fixation or hijacking
- Metadata access control bypass (users writing `AppMetadata`)
- SQL injection or other injection vulnerabilities
- Multi-tenancy isolation bypass

Out of scope:
- Vulnerabilities in dependencies (report those upstream)
- Issues that require the attacker to already have admin access
- Rate limiting bypasses that require significant infrastructure

## Credits

We publicly credit researchers who report valid vulnerabilities, unless they prefer to remain anonymous.
