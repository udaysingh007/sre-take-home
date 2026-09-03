# Observability and SLOs

## Instrumentation

The Candidate API is monitored via the existing Prometheus + Grafana stack deployed on the k3s cluster. Key metrics are collected through:

- **kube-state-metrics**: Deployment status, replica counts, pod conditions
- **node-exporter**: Host-level CPU, memory, disk, network
- **Kubernetes pod metrics**: Container CPU/memory usage, restarts
- **Probe-based health**: Liveness and readiness probe success/failure rates

## Service Level Indicators (SLIs)

### SLI 1: Availability

**Definition**: The proportion of successful HTTP requests (non-5xx) to the Candidate API.

```
SLI = 1 - (count of 5xx responses / total requests)
```

**Measured via**: Traefik ingress metrics (`traefik_service_requests_total` with `code` label).

### SLI 2: Latency

**Definition**: The proportion of requests served within an acceptable duration.

```
SLI = count of requests with latency < 500ms / total requests
```

**Measured via**: Traefik request duration histogram (`traefik_service_request_duration_seconds_bucket`).

### SLI 3: Readiness

**Definition**: The proportion of time the service reports itself as ready (all dependencies healthy).

```
SLI = time readiness probe succeeds / total time
```

**Measured via**: `kube_pod_status_ready` from kube-state-metrics.

## Service Level Objectives (SLOs)

| SLO | Target | Window | Error Budget |
|-----|--------|--------|-------------|
| Availability | 99.9% | 30 days | 43.2 minutes/month |
| Latency (p99 < 500ms) | 99.5% | 30 days | 3.6 hours/month |
| Readiness | 99.9% | 30 days | 43.2 minutes/month |

### Error Budget

For the availability SLO (99.9% over 30 days):

- **Total minutes in window**: 43,200
- **Allowed downtime**: 43.2 minutes
- **Budget consumed formula**: `(actual_downtime / 43.2) * 100%`

When error budget consumption exceeds:
- **50%**: Review recent deployments and incidents
- **75%**: Halt non-critical deployments, prioritize reliability work
- **100%**: Full deployment freeze until budget recovers

### Burn Rate Alerting

Using multi-window burn rate alerts (Google SRE approach):

| Severity | Burn Rate | Long Window | Short Window | Time to Exhaust Budget |
|----------|-----------|-------------|--------------|----------------------|
| Critical | 14.4x | 1 hour | 5 minutes | ~2.1 hours |
| Warning | 6x | 6 hours | 30 minutes | ~5 hours |
| Ticket | 1x | 3 days | 6 hours | 30 days |

**Critical alert**: Fires when both the 1-hour and 5-minute windows show a burn rate exceeding 14.4x. This means the error budget would be exhausted in ~2 hours at the current rate.

**Warning alert**: Fires when 6-hour and 30-minute windows show 6x burn rate. Indicates a slower but sustained degradation.

**Ticket**: Creates a ticket (non-paging) when the 3-day burn rate exceeds 1x, meaning the budget will be exhausted before the window ends.

## Grafana Dashboard

A dashboard for the Candidate API SLIs is provisioned in Grafana covering:

1. **Request Rate**: Total requests per second by status code
2. **Error Rate**: 5xx responses as percentage of total
3. **Latency Distribution**: p50, p90, p99 request durations
4. **Availability SLI**: Rolling 30-day availability percentage
5. **Error Budget Remaining**: Percentage of error budget consumed
6. **Pod Health**: Ready/not-ready pod count, restart count
7. **Resource Utilization**: CPU and memory usage vs limits

The dashboard JSON can be found in the Grafana instance at `/grafana` or imported via the Grafana API.

## Structured Logging

The .NET API uses the built-in ASP.NET Core structured logging with JSON output in production. Key fields:

- `timestamp`: ISO 8601 UTC
- `level`: Log level (Information, Warning, Error)
- `message`: Human-readable message
- `traceId`: Distributed trace correlation ID
- `requestPath`: HTTP request path
- `statusCode`: HTTP response status
- `elapsed`: Request duration in ms

For log aggregation, Loki could be deployed alongside Prometheus (using the same Helm chart pattern) to collect container stdout/stderr logs from all pods. The existing Grafana instance can query Loki as an additional datasource.
