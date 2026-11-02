import type { Resources } from '../en'
import account from './account.json'
import admin from './admin.json'
import auth from './auth.json'
import common from './common.json'
import media from './media.json'
import settings from './settings.json'

// Ίδια keys με τα αγγλικά: αν λείπει μετάφραση, το TypeScript το βγάζει λάθος εδώ
const el: Resources = { common, auth, account, admin, media, settings }

export default el
