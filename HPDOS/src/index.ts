import './app.css'
import { mount, unmount } from 'svelte'
import App from './App.svelte'
import { appRegistry } from './lib/apps/index'
import { helloManifest } from './lib/apps/hello/manifest'
import { appRecorderManifest } from './lib/apps/app-recorder/manifest'

appRegistry.register(helloManifest)
appRegistry.register(appRecorderManifest)

let app: ReturnType<typeof mount> | undefined

const root = document.getElementById('root')!
if (!globalThis.didMount) {
  app = mount(App, { target: root })
}
globalThis.didMount = true

if (import.meta.hot) {
  import.meta.hot.accept(async () => {
    if (!app) return
    const prev = app
    app = undefined
    await unmount(prev, { outro: true })
    app = mount(App, { target: root })
  })
}

declare global {
  var didMount: boolean | undefined
}
