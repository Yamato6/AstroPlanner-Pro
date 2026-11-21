# Security policy

## Supported versions

Only the latest release of AstroPlanner Pro receives fixes.

## Reporting a vulnerability

Please **do not open a public issue** for security problems. Report them privately through
GitHub: **Security → Report a vulnerability** on this repository
([private vulnerability reporting](https://docs.github.com/code-security/security-advisories/guidance-on-reporting-and-writing-information-about-vulnerabilities/privately-reporting-a-security-vulnerability)).

Include what you found, how to reproduce it and the version affected. You should get an answer
within a week; once a fix is released the advisory is published with credit to you, unless you
prefer otherwise.

## Scope

AstroPlanner Pro is a desktop app without accounts or a server. Relevant reports include, for
example: a way to make it run code or open a link outside its allow-list, the sky renderer loading
remote content, data sent to a service beyond what [docs/external-services.md](docs/external-services.md)
describes, or a tampered release artifact (check downloads against `SHA256SUMS.txt`).
