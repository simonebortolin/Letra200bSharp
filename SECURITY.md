# Security Policy

## Supported Versions

Only the latest release line receives security fixes. Fixes ship as a new release from `main`;
older versions are not patched.

| Version | Supported          |
| ------- | ------------------ |
| 2.0.x   | :white_check_mark: |
| < 2.0   | :x:                |

Pull request pre-releases (`pr-<number>-...`) are untested builds of unmerged changes and are
never supported.

## Reporting a Vulnerability

Please **don't open a public issue** for a security problem. Report it privately through
GitHub's [private vulnerability reporting](https://github.com/simonebortolin/Letra200bSharp/security/advisories/new)
instead, including:

- the affected version (see the app's About tab) and platform (Windows, Linux or Android);
- what an attacker could do, and the steps to reproduce it;
- a proof of concept, if you have one.

This is a hobby project maintained in spare time: reports are answered on a best-effort basis.
If the report is confirmed, a fix is released and a GitHub security advisory is published,
crediting you unless you prefer otherwise. If it's declined, you'll get an explanation of why.

Vulnerabilities in third-party dependencies (e.g. SkiaSharp, InTheHand.BluetoothLE, Avalonia)
should be reported to their own projects; once fixed upstream, the dependency is updated here.
