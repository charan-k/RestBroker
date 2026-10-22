# Changelog

All notable changes to this project are documented in this file.

## [Unreleased]

### Added
- Add .NET 8/NUnit API and private UI test suites, with run-scoped environment provisioning and cleanup in GitHub Actions.

### Changed
- Document API and private UI test setup and execution in the README.

### Fixed
- Configure `ROOM_API` during the UI assets build without rejecting a Dockerfile that defines it in another build stage.
