# Alert Migration Status

## ✅ Completed

### SDK Implementation
- ✅ Added `CreateAlertRuleAsync()` to GrafanaClient.cs
- ✅ Added `PostAlertRulesAsync()` to DeployPublisher.cs  
- ✅ Integrated alert rule provisioning into PublishGrafana pipeline
- ✅ Created alertrules directory structure

### Alert Rules Created
1. ✅ `pcs-work-item-success-rate.alert.json` - Monitors PCS work item success rate, alerts when < 74%
2. ✅ `pcs-exceptions-high.alert.json` - Monitors exception count, alerts when > 15 exceptions

## 📋 Remaining Alerts to Convert

### From arcadeAvailability.dashboard.json
3. ⏳ PCS Background Worker Stopped - Alerts when work item processing stops (< 20 items)
4. ⏳ PCS Disk Space Issues alert - Monitors disk space availability
5. ⏳ Git Push success rate alert - Tracks git operation success
6. ⏳ Container job execution failures alert - Azure DevOps pipeline failures
7. ⏳ Helix API availability - API health check
8. ⏳ Helix API Average Response Time - Performance monitoring
9. ⏳ Helix AutoScaler Service Stopped Running - Service health
10. ⏳ DotNetEng Status Failed Requests/Hour alert - HTTP error tracking
11. ⏳ source.dot.net Availability - Website uptime

### From quota.dashboard.json  
12. ⏳ Alert 1 (TBD - need to extract)
13. ⏳ Alert 2 (TBD - need to extract)
14. ⏳ Alert 3 (TBD - need to extract)
15. ⏳ Alert 4 (TBD - need to extract)

## 🔄 Alert Migration Process

Each alert requires:

1. **Extract from dashboard JSON**
   - Find the panel with `"alert": {}` block
   - Extract `alert.name`, `alert.message`, `alert.conditions`, `alert.notifications`
   - Extract `targets` array (queries)

2. **Convert to unified alerting format**
   - Create new `.alert.json` file with kebab-case uid
   - Convert queries to `data` array
   - Add reduce expression (refId: B) - extracts last value from time series
   - Add threshold expression (refId: C) - applies condition
   - Map state: `keep_state` → `KeepLast`, `ok` → `OK`, `alerting` → `Alerting`
   - Convert `for` duration (e.g., "5m")
   - Convert `frequency` to `intervalSeconds` (e.g., "1m" → 60)
   - Move `alertRuleTags` to `labels`
   - Move `message` to `annotations.description`
   - Reference `folderUID`: "arcade-services"

3. **Handle notifications**
   - Legacy: `"notifications": [{"uid": "statusHook"}]`
   - Unified: Grafana automatically routes based on notification policy
   - Contact points already created: "statusHook", "Teams Alert", etc.

4. **Create for both environments**
   - Copy to `alertrules/Staging/`
   - Copy to `alertrules/Production/`
   - Parameters auto-replaced during deployment

5. **Remove from dashboard**
   - Delete entire `"alert": {}` block from panel
   - Keep `thresholds` array for visual indicators

## 🎯 Example Alert Structure

```json
{
  "uid": "alert-name-kebab-case",
  "title": "Alert Display Name",
  "condition": "C",
  "data": [
    {
      "refId": "A",
      "queryType": "Azure Log Analytics",
      "azureLogAnalytics": {
        "query": "KQL query here",
        "resource": "[parameter(...)]"
      },
      "datasourceUid": "F2XodEi7z",
      "relativeTimeRange": {
        "from": 300,
        "to": 0
      }
    },
    {
      "refId": "B",
      "queryType": "",
      "datasourceUid": "-100",
      "model": {
        "expression": "A",
        "reducer": "last",
        "type": "reduce"
      }
    },
    {
      "refId": "C",
      "queryType": "",
      "datasourceUid": "-100",
      "model": {
        "expression": "B",
        "type": "threshold",
        "conditions": [{
          "evaluator": {"params": [threshold], "type": "lt|gt"},
          "type": "query"
        }]
      }
    }
  ],
  "noDataState": "KeepLast|OK|NoData|Alerting",
  "execErrState": "KeepLast|Alerting",
  "for": "5m",
  "annotations": {
    "description": "Alert message with @mentions"
  },
  "labels": {
    "NotificationId": "unique-id"
  },
  "folderUID": "arcade-services",
  "ruleGroup": "PCS Alerts",
  "intervalSeconds": 60,
  "isPaused": false
}
```

## 🚀 Testing Alert Rules

After provisioning:

1. **Verify in Grafana UI**:
   ```
   Navigate to: Alerting → Alert rules
   Expected: See "PCS Work Item Success Rate alert", "PCS Exceptions High"
   ```

2. **Check alert evaluation**:
   ```
   Each alert should show:
   - State: OK / Firing / Pending / NoData
   - Last evaluation time
   - Next evaluation time
   ```

3. **Test notifications**:
   ```
   - Wait for alert to fire naturally, OR
   - Temporarily lower threshold to trigger alert
   - Verify notification sent to contact point
   ```

4. **View alert history**:
   ```
   Navigate to: Alerting → Alert instances
   See firing history and state changes
   ```

## 📝 Notes

- Contact points (statusHook, Teams Alert) already created and working
- Notification routing happens automatically via notification policies
- Alert rules are independent of dashboards
- Can have multiple alerts on same query
- Supports complex multi-condition logic via expression queries

## ⚠️ Current State

**IMPORTANT**: Only 2 of 15+ alerts have been migrated so far. The remaining alerts need to be converted following the same pattern as the two examples.

The SDK is ready - it will automatically pick up any new `.alert.json` files added to the `alertrules/Staging/` or `alertrules/Production/` directories.

## 🔧 Quick Reference

**Convert frequency to seconds**:
- "1m" → 60
- "5m" → 300
- "1h" → 3600

**State mapping**:
- `keep_state` → `KeepLast`
- `alerting` → `Alerting`
- `ok` → `OK`
- `no_data` → `NoData`

**Condition operators**:
- `lt` = less than (<)
- `gt` = greater than (>)
- `within_range` = between two values
- `outside_range` = outside range

**Reducer functions**:
- `last` = most recent value
- `avg` = average
- `min` = minimum
- `max` = maximum
- `sum` = sum
