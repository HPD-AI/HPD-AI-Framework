export const manifest = (() => {
function __memo(fn) {
	let value;
	return () => value ??= (value = fn());
}

return {
	appDir: "_app",
	appPath: "_app",
	assets: new Set([]),
	mimeTypes: {},
	_: {
		client: {start:"_app/immutable/entry/start.BMxhNttP.js",app:"_app/immutable/entry/app.CdlvUYJs.js",imports:["_app/immutable/entry/start.BMxhNttP.js","_app/immutable/chunks/CBTKVbMn.js","_app/immutable/chunks/h9PxOPTP.js","_app/immutable/chunks/ClAjQE2y.js","_app/immutable/entry/app.CdlvUYJs.js","_app/immutable/chunks/h9PxOPTP.js","_app/immutable/chunks/C-eYwo0O.js","_app/immutable/chunks/CN0IhaTe.js","_app/immutable/chunks/ClAjQE2y.js","_app/immutable/chunks/BYlxzley.js","_app/immutable/chunks/BFyugDP8.js"],stylesheets:[],fonts:[],uses_env_dynamic_public:false},
		nodes: [
			__memo(() => import('./nodes/0.js')),
			__memo(() => import('./nodes/1.js')),
			__memo(() => import('./nodes/2.js'))
		],
		remotes: {
			
		},
		routes: [
			{
				id: "/",
				pattern: /^\/$/,
				params: [],
				page: { layouts: [0,], errors: [1,], leaf: 2 },
				endpoint: null
			}
		],
		prerendered_routes: new Set([]),
		matchers: async () => {
			
			return {  };
		},
		server_assets: {}
	}
}
})();
