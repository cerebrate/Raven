#!/bin/sh
# Raven.Core container entrypoint
#
# Exit code semantics (defined in ExitCodes.cs):
#   0  = admin-requested shutdown    → sleep indefinitely so Kubernetes does NOT restart
#   42 = admin-requested restart     → exit so Kubernetes restart policy relaunches the pod
#   *  = unexpected error            → exit so Kubernetes restart policy relaunches the pod

dotnet /app/Raven.Core.dll
EXIT_CODE=$?

if [ "$EXIT_CODE" -eq 0 ]; then
  # Admin requested a clean shutdown. Under Kubernetes, any container exit triggers
  # a pod restart depending on the restart policy. We want to honour the explicit
  # "stop — do not restart" intent, so we keep the container alive but idle by
  # sleeping indefinitely. An operator can delete the pod manually if needed.
  echo ""
  echo "============================================================"
  echo "  RAVEN SHUTDOWN REQUESTED BY ADMIN — POD WILL NOT RESTART"
  echo "  Delete this pod manually to remove it from the cluster."
  echo "============================================================"
  echo ""
  sleep infinity
else
  # Exit code 42: admin-requested restart.
  # Any other non-zero code: unexpected error.
  # In both cases, exit the container so the Kubernetes restart policy can
  # relaunch the pod. Preserve the original exit code so monitoring tools
  # can distinguish a deliberate restart (42) from an error.
  exit "$EXIT_CODE"
fi
