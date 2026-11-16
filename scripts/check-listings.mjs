#!/usr/bin/env node
/**
 * Validates store-assets/microsoft/listings.json against the Microsoft Store limits (LIST-3, LIST-5).
 *
 * Fails, never truncates: over-length text is an error to fix in the source. Also enforces the
 * product name (D-012) and that the README header tagline is the first sentence of the
 * short description (LIST-12), so the README and the listing cannot drift apart.
 *
 * Usage: node scripts/check-listings.mjs [path-to-listings.json]
 * Exits 1 and lists every problem when the file is invalid.
 */
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname, join } from 'node:path'

const repoRoot = join(dirname(fileURLToPath(import.meta.url)), '..')

/** Microsoft Store limits from LIST-5. */
export const LIMITS = {
  shortDescription: 270,
  fullDescription: 4000,
  featureCount: 20,
  featureLength: 200,
  keywordCount: 7,
  keywordLength: 40,
  keywordWords: 21,
  captionLength: 200,
}

/** The product name every locale's title must be (D-012). */
export const PRODUCT_NAME = 'ImmichDrive'

/** Names the product no longer uses anywhere in listing text (BRAND-1). */
const RETIRED_NAMES = ['Drive for Immich', 'Immich Drive']

/**
 * Checks one parsed listings document.
 * @param {object} listings the parsed listings.json
 * @param {string|null} readmeTagline the README header tagline, or null to skip that check
 * @returns {string[]} problems found; empty when valid
 */
export function checkListings(listings, readmeTagline) {
  const problems = []

  if (listings?.schemaVersion !== 1) {
    problems.push('schemaVersion must be 1')
  }

  const locales = listings?.locales ?? {}
  const identities = Object.keys(locales)
  if (!identities.includes('en-US')) {
    problems.push('locales must include en-US')
  }

  for (const identity of identities) {
    const record = locales[identity]

    // An alias locale copies another record and carries no fields of its own (LIST-3).
    if (record && typeof record.copyFrom === 'string') {
      if (!locales[record.copyFrom] || locales[record.copyFrom].copyFrom) {
        problems.push(`${identity}: copyFrom must name a locale with its own record`)
      }
      continue
    }

    problems.push(...checkRecord(identity, record ?? {}))
  }

  const english = locales['en-US']
  if (readmeTagline !== null && english && !english.copyFrom) {
    const firstSentence = firstSentenceOf(english.shortDescription ?? '')
    if (firstSentence !== readmeTagline) {
      problems.push(
        `README tagline must equal the first sentence of the en-US shortDescription (LIST-12):\n` +
          `  README:  ${readmeTagline}\n  listing: ${firstSentence}`,
      )
    }
  }

  return problems
}

/** Checks one locale record's fields and limits. */
function checkRecord(identity, record) {
  const problems = []
  const at = (field) => `${identity}.${field}`

  if (record.title !== PRODUCT_NAME) {
    problems.push(`${at('title')} must be "${PRODUCT_NAME}"`)
  }

  for (const field of ['shortDescription', 'fullDescription']) {
    const text = record[field]
    if (typeof text !== 'string' || text.trim() === '') {
      problems.push(`${at(field)} is required`)
    } else if (text.length > LIMITS[field]) {
      problems.push(`${at(field)} is ${text.length} characters; the limit is ${LIMITS[field]}`)
    }
  }

  problems.push(...checkList(at('features'), record.features, LIMITS.featureCount, LIMITS.featureLength, true))
  problems.push(...checkList(at('keywords'), record.keywords, LIMITS.keywordCount, LIMITS.keywordLength, false))
  problems.push(...checkList(at('screenshotCaptions'), record.screenshotCaptions, Infinity, LIMITS.captionLength, true))

  if (Array.isArray(record.keywords)) {
    const words = record.keywords.join(' ').split(/\s+/).filter(Boolean).length
    if (words > LIMITS.keywordWords) {
      problems.push(`${at('keywords')} has ${words} words combined; the limit is ${LIMITS.keywordWords}`)
    }
  }

  // Retired product names must not appear in any user-readable field.
  const text = JSON.stringify(record)
  for (const name of RETIRED_NAMES) {
    if (text.includes(name)) {
      problems.push(`${identity}: uses the retired name "${name}"; write "${PRODUCT_NAME}"`)
    }
  }

  return problems
}

/** Checks a list field's type, count, entry type and entry length. */
function checkList(label, value, maxCount, maxLength, required) {
  if (!Array.isArray(value)) {
    return [`${label} must be a list`]
  }

  const problems = []
  if (required && value.length === 0) {
    problems.push(`${label} needs at least one entry`)
  }
  if (value.length > maxCount) {
    problems.push(`${label} has ${value.length} entries; the limit is ${maxCount}`)
  }
  value.forEach((entry, index) => {
    if (typeof entry !== 'string' || entry.trim() === '') {
      problems.push(`${label}[${index}] must be non-empty text`)
    } else if (entry.length > maxLength) {
      problems.push(`${label}[${index}] is ${entry.length} characters; the limit is ${maxLength}`)
    }
  })
  return problems
}

/** The first sentence of a description: text up to and including the first full stop. */
export function firstSentenceOf(text) {
  const end = text.indexOf('. ')
  return end === -1 ? text.trim() : text.slice(0, end + 1).trim()
}

/** The tagline in the README's centred header: the paragraph after the <h1>, tags removed. */
export function readmeTaglineOf(readme) {
  const match = readme.match(/<\/h1>\s*<p[^>]*>([\s\S]*?)<\/p>/)
  if (!match) {
    return null
  }
  return match[1].replace(/<[^>]+>/g, '').replace(/\s+/g, ' ').trim()
}

// Run as a script: validate the repo's file.
if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const path = process.argv[2] ?? join(repoRoot, 'store-assets/microsoft/listings.json')
  const listings = JSON.parse(readFileSync(path, 'utf8'))
  const tagline = readmeTaglineOf(readFileSync(join(repoRoot, 'README.md'), 'utf8'))
  if (tagline === null) {
    console.error('check-listings: could not find the README header tagline')
    process.exit(1)
  }

  const problems = checkListings(listings, tagline)
  if (problems.length > 0) {
    console.error(`check-listings: ${problems.length} problem(s) in ${path}`)
    for (const problem of problems) {
      console.error(`  - ${problem}`)
    }
    process.exit(1)
  }
  console.log(`check-listings: ${Object.keys(listings.locales).join(', ')} valid`)
}
