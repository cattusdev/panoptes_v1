import account from './account.json'
import admin from './admin.json'
import auth from './auth.json'
import common from './common.json'
import media from './media.json'
import settings from './settings.json'

// Τα αγγλικά είναι η "πηγή": τα keys και οι τύποι όλων των άλλων γλωσσών βγαίνουν από εδώ
const en = { common, auth, account, admin, media, settings }

export type Resources = typeof en

export default en
