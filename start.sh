#!/bin/sh
cd "$(dirname "$0")" || exit 1
export TRAVEL_DATA_DIR="$PWD/DataFiles"
exec dotnet run --project TravelAgency.csproj --no-launch-profile
