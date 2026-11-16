// Tests for scripts/check-listings.mjs. Run by `npm test` (node --test).
import { test } from 'node:test'
import assert from 'node:assert/strict'
import { checkListings, firstSentenceOf, readmeTaglineOf, LIMITS } from './check-listings.mjs'

const TAGLINE = 'Browse your photos in Explorer.'

/** A minimal valid document; each test breaks one thing. */
function valid() {
  return {
    schemaVersion: 1,
    locales: {
      'en-US': {
        title: 'ImmichDrive',
        shortDescription: `${TAGLINE} Requires a server.`,
        fullDescription: 'ImmichDrive shows your library.',
        features: ['Thumbnails without downloading'],
        keywords: ['immich', 'photos'],
        screenshotCaptions: ['Tray flyout'],
      },
    },
  }
}

test('a valid document has no problems', () => {
  assert.deepEqual(checkListings(valid(), TAGLINE), [])
})

test('over-length text is rejected, not truncated', () => {
  const doc = valid()
  doc.locales['en-US'].shortDescription = `${TAGLINE} ${'x'.repeat(LIMITS.shortDescription)}`
  assert.match(checkListings(doc, TAGLINE).join('\n'), /shortDescription is \d+ characters; the limit is 270/)
})

test('too many features and an over-long feature are both reported', () => {
  const doc = valid()
  doc.locales['en-US'].features = Array.from({ length: 21 }, () => 'f')
  doc.locales['en-US'].features[0] = 'y'.repeat(201)
  const problems = checkListings(doc, TAGLINE).join('\n')
  assert.match(problems, /features has 21 entries/)
  assert.match(problems, /features\[0\] is 201 characters/)
})

test('keyword count, length and combined words are limited', () => {
  const doc = valid()
  doc.locales['en-US'].keywords = ['a b c d e f g h', 'a b c d e f g h', 'a b c d e f g h', 'z'.repeat(41), 'k', 'k', 'k', 'k']
  const problems = checkListings(doc, TAGLINE).join('\n')
  assert.match(problems, /keywords has 8 entries/)
  assert.match(problems, /keywords\[3\] is 41 characters/)
  assert.match(problems, /words combined/)
})

test('keywords may be empty, but features and captions may not', () => {
  const doc = valid()
  doc.locales['en-US'].keywords = []
  doc.locales['en-US'].features = []
  const problems = checkListings(doc, TAGLINE).join('\n')
  assert.doesNotMatch(problems, /keywords/)
  assert.match(problems, /features needs at least one entry/)
})

test('the title must be the product name and retired names are rejected', () => {
  const doc = valid()
  doc.locales['en-US'].title = 'Drive for Immich'
  doc.locales['en-US'].fullDescription = 'Immich Drive shows your library.'
  const problems = checkListings(doc, TAGLINE).join('\n')
  assert.match(problems, /title must be "ImmichDrive"/)
  assert.match(problems, /retired name "Drive for Immich"/)
  assert.match(problems, /retired name "Immich Drive"/)
})

test('the README tagline must match the first sentence of the short description', () => {
  assert.match(checkListings(valid(), 'Something else.').join('\n'), /README tagline must equal/)
})

test('an alias locale must point at a locale with its own record', () => {
  const doc = valid()
  doc.locales['en-CA'] = { copyFrom: 'en-US' }
  assert.deepEqual(checkListings(doc, TAGLINE), [])
  doc.locales['fr-CA'] = { copyFrom: 'fr-FR' }
  assert.match(checkListings(doc, TAGLINE).join('\n'), /fr-CA: copyFrom/)
})

test('a missing en-US locale and a wrong schema version are reported', () => {
  const problems = checkListings({ schemaVersion: 2, locales: {} }, null).join('\n')
  assert.match(problems, /schemaVersion must be 1/)
  assert.match(problems, /must include en-US/)
})

test('helpers read the first sentence and the README header tagline', () => {
  assert.equal(firstSentenceOf('One two. Three.'), 'One two.')
  const readme = '<h1 align="center">X</h1>\n\n<p align="center">\n  Your <a href="u">Immich</a> photos.\n</p>'
  assert.equal(readmeTaglineOf(readme), 'Your Immich photos.')
})
