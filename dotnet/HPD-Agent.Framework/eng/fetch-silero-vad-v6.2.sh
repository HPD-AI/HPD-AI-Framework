#!/usr/bin/env bash
set -euo pipefail

readonly commit="be95df9152c0d7618fa1edfeb296fc3dae32376f"
readonly expected="1a153a22f4509e292a94e67d6f9b85e8deb25b4988682b7e174c65279d8788e3"
readonly root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readonly directory="${root}/artifacts/silero-vad/v6.2"
readonly destination="${directory}/silero_vad.onnx"
readonly temporary="${destination}.download"
readonly corpus="${directory}/test.wav"
readonly corpus_temporary="${corpus}.download"
readonly corpus_expected="89f17d9c94c4b31eb320f424628bcbc920abaddbee6e2760fd868bfb1d9a2e47"

mkdir -p "${directory}"
if [[ ! -f "${destination}" ]] || [[ "$(shasum -a 256 "${destination}" | awk '{print $1}')" != "${expected}" ]]; then
  curl --fail --location --silent --show-error \
    "https://raw.githubusercontent.com/snakers4/silero-vad/${commit}/src/silero_vad/data/silero_vad.onnx" \
    --output "${temporary}"
  readonly actual="$(shasum -a 256 "${temporary}" | awk '{print $1}')"
  if [[ "${actual}" != "${expected}" ]]; then
    printf 'Silero model digest mismatch: expected %s, found %s\n' "${expected}" "${actual}" >&2
    exit 1
  fi
  mv "${temporary}" "${destination}"
fi

if [[ ! -f "${corpus}" ]] || [[ "$(shasum -a 256 "${corpus}" | awk '{print $1}')" != "${corpus_expected}" ]]; then
  curl --fail --location --silent --show-error \
    "https://raw.githubusercontent.com/snakers4/silero-vad/${commit}/tests/data/test.wav" \
    --output "${corpus_temporary}"
  readonly corpus_actual="$(shasum -a 256 "${corpus_temporary}" | awk '{print $1}')"
  if [[ "${corpus_actual}" != "${corpus_expected}" ]]; then
    printf 'Silero corpus digest mismatch: expected %s, found %s\n' "${corpus_expected}" "${corpus_actual}" >&2
    exit 1
  fi
  mv "${corpus_temporary}" "${corpus}"
fi
printf '%s\n' "${destination}"
