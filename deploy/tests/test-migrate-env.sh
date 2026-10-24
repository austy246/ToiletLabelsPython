#!/usr/bin/env bash
# Test for deploy/migrate-env.sh. Run: bash deploy/tests/test-migrate-env.sh
set -uo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
script="$here/../migrate-env.sh"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

fail=0
ok()   { echo "ok   - $1"; }
bad()  { echo "FAIL - $1"; fail=1; }
check() { # description, command...
  local desc="$1"; shift
  if "$@"; then ok "$desc"; else bad "$desc"; fi
}
has_line()  { grep -Fxq -- "$2" "$1"; }
no_line()   { ! grep -Fxq -- "$2" "$1"; }
no_prefix() { ! grep -q -- "^$2" "$1"; }

CONN='DefaultEndpointsProtocol=https;AccountName=toiletlabelsstorage;AccountKey=abc==;EndpointSuffix=core.windows.net'
env_file="$work/.env"
write_env() {
  cat > "$env_file" <<EOF2
COMPOSE_PROJECT_NAME=toiletlabels
DJANGO_SECRET_KEY=s3cret
DJANGO_ALLOWED_HOSTS=toiletlabels.austy.cz,localhost
CSRF_TRUSTED_ORIGINS=https://toiletlabels.austy.cz
DJANGO_BEHIND_PROXY=True
AZURE_STORAGE_CONNECTION_STRING=$CONN
DJANGO_SUPERUSER_PASSWORD=pw
MAPY_API_KEY=k
APP_IMAGE=x
EOF2
}

write_env
out1="$work/out1.txt"
bash "$script" "$env_file" > "$out1" 2>&1
check "first run exits 0" test $? -eq 0

check "APP_DOMAIN"          has_line "$env_file" "APP_DOMAIN=toiletlabels.austy.cz"
check "AllowedHosts"        has_line "$env_file" "AllowedHosts=toiletlabels.austy.cz"
check "Storage__PublicBlobBaseUrl" has_line "$env_file" \
  "Storage__PublicBlobBaseUrl=https://toiletlabelsstorage.blob.core.windows.net/toiletlabels/"
check "Admin__UserName default" has_line "$env_file" "Admin__UserName=austy"
check "Admin__Password"     has_line "$env_file" "Admin__Password=pw"
check "Mapy__ApiKey"        has_line "$env_file" "Mapy__ApiKey=k"
check "ASPNETCORE_ENVIRONMENT" has_line "$env_file" "ASPNETCORE_ENVIRONMENT=Production"
check "ConnectionStrings__Storage copied intact (incl. ==)" has_line "$env_file" "ConnectionStrings__Storage=$CONN"
check "legacy connection string left untouched" has_line "$env_file" "AZURE_STORAGE_CONNECTION_STRING=$CONN"
check "APP_IMAGE untouched" has_line "$env_file" "APP_IMAGE=x"
# File modes are not meaningful on Windows filesystems (Git Bash), so only check them elsewhere.
case "$(uname -s)" in
  MINGW*|MSYS*|CYGWIN*) echo "skip - file modes (Windows filesystem)" ;;
  *) check "env file mode is 600" bash -c '[ "$(stat -c %a "$1")" = 600 ]' _ "$env_file" ;;
esac
check "backup created" bash -c 'ls "$1"/.env.bak-* >/dev/null 2>&1' _ "$work"

check "stdout hides connection string" bash -c '! grep -q "abc==" "$1"' _ "$out1"
check "stdout hides password"          bash -c '! grep -qw "pw" "$1"' _ "$out1"
check "stdout hides api key value"     bash -c '! grep -q "=k\b" "$1"' _ "$out1"

# Idempotence: a second run changes nothing and creates no new backup.
sum1="$(sha256sum "$env_file" | cut -d' ' -f1)"
nbak1="$(ls "$work"/.env.bak-* | wc -l)"
out2="$work/out2.txt"
bash "$script" "$env_file" > "$out2" 2>&1
sum2="$(sha256sum "$env_file" | cut -d' ' -f1)"
check "second run leaves the file unchanged" test "$sum1" = "$sum2"
check "second run creates no new backup" test "$nbak1" = "$(ls "$work"/.env.bak-* | wc -l)"
check "second run stdout hides secrets" bash -c '! grep -Eq "abc==|\bpw\b" "$1"' _ "$out2"

# Existing new keys are never overwritten.
printf 'Admin__UserName=custom\n' >> "$env_file"
sed -i '/^Admin__UserName=austy$/d' "$env_file"
bash "$script" "$env_file" > /dev/null 2>&1
check "existing Admin__UserName kept" has_line "$env_file" "Admin__UserName=custom"
check "no second Admin__UserName"     test "$(grep -c '^Admin__UserName=' "$env_file")" = 1

# --drop-legacy removes the Django keys but keeps the rest.
out3="$work/out3.txt"
bash "$script" --drop-legacy "$env_file" > "$out3" 2>&1
check "--drop-legacy exits 0" test $? -eq 0
check "DJANGO_* removed"  no_prefix "$env_file" "DJANGO_"
check "AZURE_STORAGE_CONNECTION_STRING removed" no_prefix "$env_file" "AZURE_STORAGE_CONNECTION_STRING="
check "MAPY_API_KEY removed" no_prefix "$env_file" "MAPY_API_KEY="
check "CSRF_TRUSTED_ORIGINS removed" no_prefix "$env_file" "CSRF_TRUSTED_ORIGINS="
check "APP_IMAGE=x kept" has_line "$env_file" "APP_IMAGE=x"
check "COMPOSE_PROJECT_NAME kept" has_line "$env_file" "COMPOSE_PROJECT_NAME=toiletlabels"
check "new keys survive --drop-legacy" has_line "$env_file" "ConnectionStrings__Storage=$CONN"
check "Mapy__ApiKey survives --drop-legacy" has_line "$env_file" "Mapy__ApiKey=k"
check "--drop-legacy stdout hides secrets" bash -c '! grep -Eq "abc==|\bpw\b" "$1"' _ "$out3"

# Without a usable source for the connection string the script fails and drops nothing.
printf 'DJANGO_ALLOWED_HOSTS=a.example,b\nMAPY_API_KEY=k\n' > "$env_file"
bash "$script" --drop-legacy "$env_file" > /dev/null 2>&1
check "missing connection string -> non-zero exit" test $? -ne 0
check "nothing dropped on failure" has_line "$env_file" "MAPY_API_KEY=k"

if [ "$fail" -eq 0 ]; then echo "ALL PASSED"; else echo "SOME TESTS FAILED"; exit 1; fi
