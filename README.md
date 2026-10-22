# RestBroker test suites

The API and UI projects are separate .NET 8 NUnit suites. The API suite uses synthetic responses and fake HTTP handlers only; it does not contact the public API or perform live booking mutations. Live public API mutation testing is permanently out of scope.

## Run the API tests

```powershell
dotnet test .\RestBroker.Api.Tests\RestBroker.Api.Tests.csproj --logger "trx;LogFileName=api.trx" --results-directory .\TestResults\Api
```

No API credentials are required for the fake-backed test suite. The fail-closed live mutation fixture is explicitly skipped and must remain disabled.

## Run the UI tests

The UI suite requires a private Restful Booker Platform instance. It refuses to default to the shared public UI and accepts only a loopback HTTP URL supplied through `RESTBOOKER_UI_BASE_URL`.

```powershell
$env:RESTBOOKER_UI_BASE_URL = 'http://127.0.0.1:<assigned-port>'
dotnet test .\RestBroker.Ui.Tests\RestBroker.Ui.Tests.csproj --logger "trx;LogFileName=ui.trx" --results-directory .\TestResults\Ui
```

The Playwright test browses the room list, submits synthetic guest details for a room, and verifies the booking confirmation. Browser execution must target the private per-run environment produced by the CI provisioner; do not point it at `https://automationintesting.online`.

## GitHub Actions

[`.github/workflows/restbroker.yml`](./.github/workflows/restbroker.yml) runs on pull requests and manual dispatch. It runs the fake-backed API suite and the private UI lifecycle/browser suite in parallel, publishes separate TRX artifacts with 7-day retention, and attempts project-scoped UI teardown even if browser tests fail.

Local checks validate the provisioning guards, shell syntax, UI configuration, and project compilation. Full Docker Compose build/readiness/teardown and Playwright browser execution are verified only by a successful GitHub Actions run. I4 and I6 remain incomplete until that CI runtime verification succeeds.
