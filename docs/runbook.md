# Incident Runbooks

## Runbook 1: Readiness Check Failure

### Detection

- **Alert**: Kubernetes readiness probe fails for candidate-api pods
- **Symptom**: `/health/ready` returns HTTP 503 with `"status": "Degraded"`
- **Impact**: Affected pods removed from service; if all pods fail, `/dev/` or `/test/` returns 503

### Diagnosis

1. **Check which pods are affected**:
   ```bash
   kubectl get pods -n <namespace> -l app=candidate-api -o wide
   ```

2. **Check readiness endpoint directly**:
   ```bash
   kubectl exec -n <namespace> <pod-name> -- curl -s localhost:8080/health/ready | jq .
   ```
   The response includes a `dependencies` array showing which dependency is unhealthy.

3. **Check pod events**:
   ```bash
   kubectl describe pod -n <namespace> <pod-name>
   ```

4. **Check pod logs**:
   ```bash
   kubectl logs -n <namespace> <pod-name> --tail=100
   ```

### Remediation

- **If a configured dependency is down** (postgres, redis, third-party-billing):
  - Verify the dependency is actually down or if it's a configuration issue
  - Check `appsettings.json` configuration in the ConfigMap
  - If the dependency is intentionally unavailable, update the ConfigMap to mark it healthy or remove it

- **If the probe itself is misconfigured**:
  - Check deployment manifest for correct probe path and port
  - Verify the container is listening on port 8080

- **To temporarily restore service**:
  ```bash
  kubectl rollout restart deployment/candidate-api -n <namespace>
  ```

### Escalation

- If the issue persists after restart: escalate to the application team
- If multiple environments are affected simultaneously: check shared infrastructure (VM health, k3s status)

---

## Runbook 2: Deployment Rollback

### Detection

- **Alert**: Health check failure in the deploy workflow (deploy-dev or deploy-test job fails)
- **Symptom**: ArgoCD shows application as `Degraded` or `OutOfSync`
- **Impact**: New version is not serving traffic (readiness probe prevents routing)

### Diagnosis

1. **Check ArgoCD application status**:
   - Navigate to `/argocd` and check the application health
   - Or via CLI: `argocd app get candidate-api-dev`

2. **Check deployment rollout status**:
   ```bash
   kubectl rollout status deployment/candidate-api -n <namespace>
   kubectl get replicasets -n <namespace> -l app=candidate-api
   ```

3. **Check new pod logs for errors**:
   ```bash
   kubectl logs -n <namespace> -l app=candidate-api --tail=50
   ```

### Remediation

- **Revert the manifest change** (preferred GitOps approach):
  ```bash
  # In the sre-take-home repo, revert the kustomization.yaml to the previous image tag
  git revert <commit-sha>
  git push
  # ArgoCD will auto-sync to the previous version
  ```

- **ArgoCD rollback** (faster, but diverges from git):
  ```bash
  argocd app rollback candidate-api-dev
  ```

- **Kubernetes rollback** (last resort):
  ```bash
  kubectl rollout undo deployment/candidate-api -n <namespace>
  ```

### Post-Incident

- Investigate why the new version failed (check logs, health endpoints)
- Fix the issue in a new PR
- Verify the fix passes PR checks before merging

---

## Runbook 3: Dependent Service Degradation

### Detection

- **Alert**: Readiness check returns `Degraded` with specific dependency marked unhealthy
- **Symptom**: API returns 503 on `/health/ready`, but `/health/live` returns 200

### Diagnosis

1. **Identify the failing dependency**:
   ```bash
   curl -s http://13.216.126.57/dev/health/ready | jq '.dependencies[] | select(.healthy == false)'
   ```

2. **Check if it's a real outage or configuration drift**:
   - The readiness check uses statically configured dependencies in `appsettings.json`
   - If the dependency status is driven by environment config, check the ConfigMap

3. **Check if other services are affected**:
   ```bash
   # Check both environments
   curl -s http://13.216.126.57/dev/health/ready | jq .
   curl -s http://13.216.126.57/test/health/ready | jq .
   ```

### Remediation

- **If the dependency is temporarily unavailable**: Wait for recovery; Kubernetes will re-add pods to service when readiness passes
- **If the dependency is permanently removed**: Update `appsettings.json` in the repo to remove it from the dependency list, deploy the change
- **If it's a configuration error**: Update the environment-specific ConfigMap and restart pods

### Communication

- Notify the application team about dependent service status
- Update the incident channel with ETA for recovery
- If customer-facing impact: follow the incident communication template

---

## Runbook 4: Node / VM Failure

### Detection

- **Alert**: All services unreachable (landing page, Grafana, ArgoCD, API endpoints)
- **Symptom**: SSH to VM fails or times out

### Diagnosis

1. **Check EC2 instance status** (from local machine):
   ```bash
   aws ec2 describe-instance-status --instance-ids i-0caa548e7436ce0ff
   ```

2. **Check if the instance is running**:
   ```bash
   aws ec2 describe-instances --instance-ids i-0caa548e7436ce0ff \
     --query 'Reservations[].Instances[].State.Name'
   ```

### Remediation

- **If instance is stopped**: Start it
  ```bash
  aws ec2 start-instances --instance-ids i-0caa548e7436ce0ff
  ```
  k3s and all workloads will auto-recover on boot.

- **If instance is terminated or irrecoverable**:
  1. Re-run Terraform: `cd terraform/infra && terraform apply`
  2. Re-run observability workflow
  3. Re-run ArgoCD install
  4. ArgoCD will auto-sync application deployments from git

### Prevention

- CloudWatch alarm on instance status checks
- Consider multi-node cluster for production workloads
