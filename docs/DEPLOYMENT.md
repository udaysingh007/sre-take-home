# Deployment Guide

## Architecture Overview

```
GitHub (sre-take-home repo)
  |
  |-- PR opened/updated --> pr.yml: build, test, pack NuGet, validate Docker build
  |-- Push to main -------> deploy.yml:
  |                            1. Build, test, pack NuGet
  |                            2. Build & push Docker image to GHCR
  |                            3. Update dev manifest (image tag) & commit
  |                            4. Wait for ArgoCD to sync dev
  |                            5. Health-check dev (/dev/health/ready)
  |                            6. Update test manifest & commit
  |                            7. Wait for ArgoCD to sync test
  |                            8. Health-check test (/test/health/ready)
  |                            9. Bump VERSION for next release
  |
ArgoCD (on k3s cluster)
  |-- watches k8s/overlays/dev/  --> syncs to dev namespace
  |-- watches k8s/overlays/test/ --> syncs to test namespace
  |
k3s Cluster (AWS EC2 - r6i.xlarge)
  |-- dev namespace:  candidate-api deployment + ingress at /dev
  |-- test namespace: candidate-api deployment + ingress at /test
  |-- monitoring:     Prometheus + Grafana at /grafana
  |-- argocd:         ArgoCD at /argocd
  |-- default:        Landing page at /
```

## Environments

| Environment | Namespace | URL Path | ASPNETCORE_ENVIRONMENT |
|-------------|-----------|----------|----------------------|
| Development | `dev`     | `/dev/`  | `Development`        |
| Test        | `test`    | `/test/` | `Test`               |

## Versioning

- `VERSION` file in repo root contains the current version (e.g., `1.0.0`)
- Each successful deploy reads the version, builds with it, then bumps minor for next release
- Docker images are tagged with the version and `latest`
- Image: `ghcr.io/udaysingh007/sre-take-home:<version>`

## Deployment Flow

### PR Workflow (`pr.yml`)

Triggered on pull requests to `main`:
1. Restores .NET dependencies
2. Builds the solution in Release mode
3. Runs unit tests
4. Packs `CandidateApi.Contracts` as a NuGet package (uploaded as artifact)
5. Builds the Docker image (validation only, not pushed)

### Deploy Workflow (`deploy.yml`)

Triggered on push to `main`:
1. **Build & Publish**: Builds, tests, creates Docker image, pushes to GHCR
2. **Deploy to Dev**: Updates `k8s/overlays/dev/kustomization.yaml` with new image tag, commits to `main`. ArgoCD auto-syncs the change to the `dev` namespace. Health check validates `/dev/health/ready` returns `Healthy`.
3. **Promote to Test**: Only runs if dev health check passes. Updates `k8s/overlays/test/kustomization.yaml`, commits. ArgoCD syncs to `test` namespace. Health check validates `/test/health/ready`.
4. **Version Bump**: Increments minor version in `VERSION` file for next release.

All automated commits use `[skip ci]` to prevent workflow re-triggers.

## Kubernetes Manifests

Uses Kustomize with base + overlays:

```
k8s/
  base/                    # Shared Deployment + Service
    deployment.yaml
    service.yaml
    kustomization.yaml
  overlays/
    dev/                   # Dev-specific config
      kustomization.yaml   # Sets namespace, image tag, env vars
      ingress.yaml         # Ingress at /dev with prefix stripping
      middleware.yaml       # Traefik StripPrefix middleware
    test/                  # Test-specific config
      kustomization.yaml
      ingress.yaml         # Ingress at /test with prefix stripping
      middleware.yaml
```

### Production Hardening

- **Resource limits**: CPU/memory requests and limits on all containers
- **Security context**: `runAsNonRoot`, `readOnlyRootFilesystem`, `allowPrivilegeEscalation: false`
- **Health probes**: Liveness (`/health/live`) and readiness (`/health/ready`) with appropriate thresholds
- **Replicas**: 2 per environment for availability

## Validating the Deployment

```bash
# Check dev environment
curl http://13.216.126.57/dev/
curl http://13.216.126.57/dev/health/live
curl http://13.216.126.57/dev/health/ready
curl http://13.216.126.57/dev/api/work-items

# Check test environment
curl http://13.216.126.57/test/
curl http://13.216.126.57/test/health/live
curl http://13.216.126.57/test/health/ready
curl http://13.216.126.57/test/api/work-items
```

The root endpoint (`/dev/` or `/test/`) returns JSON with service metadata including the environment name and deployed version.

## Prerequisites

- AWS EC2 instance running k3s (provisioned via Terraform in [iac-for-coterie](https://github.com/udaysingh007/iac-for-coterie))
- ArgoCD installed on the cluster
- Traefik ingress controller (bundled with k3s)
- GitHub repository secrets: none required (GHCR uses `GITHUB_TOKEN`)
- GitHub repository variables: `VM_PUBLIC_IP` (for health checks)

## Assumptions and Tradeoffs

1. **Single-node k3s**: Acceptable for assessment; production would use multi-node with HA control plane
2. **ArgoCD auto-sync**: Immediate sync on manifest change; production might use manual sync gates for test/staging
3. **GHCR public packages**: Docker images are publicly accessible; production would use private registry with imagePullSecrets
4. **Version bump in CI**: Simple approach; production might use semantic versioning with conventional commits
5. **Health check polling**: CI waits up to 2 minutes for ArgoCD sync + pod readiness; could be optimized with ArgoCD webhook for instant sync notification
6. **NuGet package**: Produced as a build artifact; not pushed to a feed since no downstream consumers exist yet
