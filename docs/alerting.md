# Alerting Strategy

## Principles

1. **Every alert must be actionable** -- if it doesn't require human intervention, it shouldn't page
2. **Alerts should fire on symptoms, not causes** -- alert on user impact (error rate, latency) rather than internal metrics (CPU usage) unless they directly correlate
3. **Use severity tiers** -- not every alert deserves a page at 3 AM
4. **Reduce alert fatigue** -- group related alerts, use appropriate thresholds, and silence expected noise during maintenance windows

## Severity Levels

| Severity | Response Time | Notification | Examples |
|----------|--------------|--------------|---------|
| **Critical (P1)** | Immediate (< 5 min) | PagerDuty / phone call | All pods down, error budget exhausted, node unreachable |
| **Warning (P2)** | Within 30 min | Slack #alerts channel | Elevated error rate, high latency, pod restart loop |
| **Info (P3)** | Next business day | Slack #ops-info | Error budget > 50% consumed, certificate expiring in 14 days |

## Alert Definitions

### Critical Alerts

**All Pods Not Ready**
- Condition: `kube_deployment_status_replicas_available{deployment="candidate-api"} == 0` for 2 minutes
- Impact: Complete service outage for the affected environment
- Runbook: See [Runbook 1: Readiness Check Failure](runbook.md#runbook-1-readiness-check-failure)

**Error Budget Exhausted**
- Condition: 30-day availability drops below 99.9% SLO target
- Impact: Service reliability is below commitment
- Action: Deployment freeze, prioritize reliability work

**Node Unreachable**
- Condition: `up{job="node-exporter"} == 0` for 5 minutes
- Impact: All services down
- Runbook: See [Runbook 4: Node / VM Failure](runbook.md#runbook-4-node--vm-failure)

### Warning Alerts

**High Error Rate**
- Condition: 5xx rate > 1% of total requests over 5 minutes
- Impact: Some users experiencing errors
- Action: Check recent deployments, review application logs

**Pod Restart Loop**
- Condition: `kube_pod_container_status_restarts_total` increases by > 3 in 10 minutes
- Impact: Service instability, potential brief outages during restarts
- Action: Check pod logs for crash reason, review recent config changes

**High Memory Usage**
- Condition: Container memory usage > 80% of limit for 10 minutes
- Impact: Risk of OOM kill and pod restart
- Action: Review for memory leaks, consider increasing limits

**Deployment Stuck**
- Condition: `kube_deployment_status_observed_generation != kube_deployment_metadata_generation` for 10 minutes
- Impact: New version not rolling out
- Action: Check pod events, image pull status, resource availability

### Info Alerts

**Error Budget Consumption > 50%**
- Condition: More than half the monthly error budget consumed
- Action: Review recent incidents, plan reliability improvements

**Disk Usage > 70%**
- Condition: Node filesystem usage exceeds 70%
- Action: Clean up old container images, check Prometheus retention

## Escalation Path

```
Alert fires
  |
  +--> Critical: PagerDuty --> On-call SRE (5 min response)
  |      |
  |      +--> Not acknowledged in 10 min --> Escalate to SRE lead
  |      +--> Not resolved in 30 min --> Escalate to engineering manager
  |
  +--> Warning: Slack #alerts --> On-call SRE (30 min response)
  |      |
  |      +--> Not acknowledged in 1 hour --> Escalate to SRE lead
  |
  +--> Info: Slack #ops-info --> Triaged in next standup
```

## Reducing Alert Fatigue

1. **Maintenance windows**: Silence alerts during planned deployments and infrastructure changes
2. **Alert grouping**: Group related alerts (e.g., all pod alerts for the same deployment) into a single notification
3. **Threshold tuning**: Start with conservative thresholds and tighten based on observed noise-to-signal ratio
4. **Dependency-aware alerting**: Don't alert on downstream symptoms when the root cause (e.g., node down) is already alerting
5. **Regular review**: Monthly review of alert frequency; if an alert fires > 5x/month without action, reconsider its value
6. **Runbook links**: Every alert includes a link to its runbook so responders can act without guessing
