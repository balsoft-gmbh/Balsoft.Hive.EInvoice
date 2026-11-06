#!/usr/bin/env bash
# Downloads the official validators the test suite uses into .tools/validators (or $1):
#   validator.jar   KoSIT validator (https://github.com/itplr-kosit/validator)
#   xrechnung/      KoSIT XRechnung configuration: XRechnung + EN 16931 scenarios, CII and UBL
#   mustang.jar     Mustang CLI: Factur-X / ZUGFeRD profile rules and PDF/A-3 (veraPDF)
#   xrechnung-model/ KoSIT XRechnung model: every business term with its CII and UBL path
#   facturx/        the Factur-X / ZUGFeRD schema of each profile, taken out of mustang.jar
# Then: export HIVE_VALIDATORS=<dir> and run dotnet test.
set -euo pipefail

KOSIT_VALIDATOR="1.6.3"
XRECHNUNG_CONFIG_TAG="v2026-08-31"
XRECHNUNG_CONFIG_ZIP="xrechnung-3.0.2-validator-configuration-2026-08-31.zip"
MUSTANG="2.26.0"
XRECHNUNG_MODEL="xrechnung-3.0.2-xrechnung-model/2026-08-31/xrechnung-3.0.2-xrechnung-model-2026-08-31.zip"
FACTURX_SCHEMA="ZF_250"

dir="${1:-$(cd "$(dirname "$0")/.." && pwd)/.tools/validators}"
mkdir -p "$dir"
cd "$dir"

[ -f validator.jar ] || curl -fsSL -o validator.jar \
  "https://github.com/itplr-kosit/validator/releases/download/v${KOSIT_VALIDATOR}/validator-${KOSIT_VALIDATOR}-standalone.jar"

if [ ! -f xrechnung/scenarios.xml ]; then
  curl -fsSL -o xrechnung.zip \
    "https://github.com/itplr-kosit/validator-configuration-xrechnung/releases/download/${XRECHNUNG_CONFIG_TAG}/${XRECHNUNG_CONFIG_ZIP}"
  mkdir -p xrechnung && (cd xrechnung && unzip -q -o ../xrechnung.zip) && rm xrechnung.zip
fi

[ -f mustang.jar ] || curl -fsSL -o mustang.jar \
  "https://github.com/ZUGFeRD/mustangproject/releases/download/core-${MUSTANG}/Mustang-CLI-${MUSTANG}.jar"

if [ ! -f xrechnung-model/xrechnung-cius-model.xml ]; then
  curl -fsSL -o xrechnung-model.zip "https://projekte.kosit.org/api/v4/projects/356/packages/maven/de/xeinkauf/${XRECHNUNG_MODEL}"
  mkdir -p xrechnung-model && unzip -q -o -j xrechnung-model.zip 'model/*.xml' -d xrechnung-model && rm xrechnung-model.zip
fi

if [ ! -f facturx/EXTENDED/FACTUR-X_EXTENDED.xsd ]; then
  for profile in MINIMUM BASIC-WL BASIC EN16931 EXTENDED; do
    mkdir -p "facturx/$profile"
    unzip -q -o -j mustang.jar "schema/${FACTURX_SCHEMA}/${profile}/*.xsd" -d "facturx/$profile"
  done
fi

echo "$dir"
