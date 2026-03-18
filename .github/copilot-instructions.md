# Copilot Instructions

## Project Guidelines
- For Raven containerized deployments, prefer `/data/workspace` as the default workspace root to keep mutable state separate from binaries in `/app`.
- For local (non-container) Raven workspace paths, include the company folder 'Arkane Systems' before 'Raven' to improve folder distinction.
- Use the repository workspace root path (C:\Working\cerebrate\Raven) explicitly when running terminal commands to avoid wrong-directory failures.