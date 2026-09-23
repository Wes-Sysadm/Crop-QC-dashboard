#!/bin/sh
# Keep this path in Render's service-level Docker Command, including during rollback.
# Pre-feature images do not contain it and therefore cannot start accidentally.
set -eu
cd /app
dotnet CropQc.Web.dll --verify-truck-receipt-schema
exec dotnet CropQc.Web.dll --urls "http://0.0.0.0:${PORT:-8080}"
