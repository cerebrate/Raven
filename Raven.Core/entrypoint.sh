#!/bin/sh
# Raven.Core container entrypoint
#
# Exit code semantics (defined in ExitCodes.cs):
#   0  = admin-requested shutdown → sleep indefinitely so Kubernetes does NOT restart the pod
#   42 = admin-requested restart  → restart the dotnet process in-place (pod stays alive)
#   *  = unexpected error         → exit so Kubernetes restart policy relaunches the pod

while true; do
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
    break
  elif [ "$EXIT_CODE" -eq 42 ]; then
    # Admin-requested restart. Relaunch the dotnet process inside this container
    # without exiting the pod — faster than a full pod recycle and avoids
    # disturbing other containers in the pod.
    echo ""
    echo "============================================================"
    echo "  RAVEN RESTART REQUESTED BY ADMIN — RESTARTING PROCESS..."
    echo "============================================================"
    echo ""
    # Loop continues: dotnet will be re-launched at the top of the while loop.
  else
    # Unexpected error — exit the container so the Kubernetes restart policy can
    # relaunch the pod. Preserve the original exit code so monitoring tools can
    # distinguish errors from deliberate lifecycle events.
    exit "$EXIT_CODE"
  fi
done
