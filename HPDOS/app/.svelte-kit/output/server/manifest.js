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
		client: {start:"_app/immutable/entry/start.WCzh0KNw.js",app:"_app/immutable/entry/app.ajU_SuSy.js",imports:["_app/immutable/entry/start.WCzh0KNw.js","_app/immutable/chunks/CPsBRTQ2.js","_app/immutable/chunks/DiyOmBHr.js","_app/immutable/chunks/p-Y5FwZQ.js","_app/immutable/entry/app.ajU_SuSy.js","_app/immutable/chunks/DiyOmBHr.js","_app/immutable/chunks/me0Zbks7.js","_app/immutable/chunks/hZqBFjZh.js","_app/immutable/chunks/p-Y5FwZQ.js","_app/immutable/chunks/BItWWJxK.js","_app/immutable/chunks/B0qMKQ4g.js"],stylesheets:[],fonts:[],uses_env_dynamic_public:false},
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
