import { a4 as hasContext, g as getContext, a2 as setContext, a5 as props_id, a6 as attributes, a7 as bind_props, a3 as derived, a8 as spread_props, a9 as head, aa as attr_class, ab as attr, ac as ensure_array_like, e as escape_html } from "../../chunks/index.js";
import { clsx } from "clsx";
const CLASS_VALUE_PRIMITIVE_TYPES = ["string", "number", "bigint", "boolean"];
function isClassValue(value) {
  if (value === null || value === void 0)
    return true;
  if (CLASS_VALUE_PRIMITIVE_TYPES.includes(typeof value))
    return true;
  if (Array.isArray(value))
    return value.every((item) => isClassValue(item));
  if (typeof value === "object") {
    if (Object.getPrototypeOf(value) !== Object.prototype)
      return false;
    return true;
  }
  return false;
}
const BoxSymbol = /* @__PURE__ */ Symbol("box");
function boxWith(getter, setter) {
  return {
    [BoxSymbol]: true,
    get current() {
      return getter();
    }
  };
}
function composeHandlers(...handlers) {
  return function(e) {
    for (const handler of handlers) {
      if (!handler)
        continue;
      if (e.defaultPrevented)
        return;
      if (typeof handler === "function") {
        handler.call(this, e);
      } else {
        handler.current?.call(this, e);
      }
    }
  };
}
var COMMENT_REGEX = /\/\*[^*]*\*+([^/*][^*]*\*+)*\//g;
var NEWLINE_REGEX = /\n/g;
var WHITESPACE_REGEX = /^\s*/;
var PROPERTY_REGEX = /^(\*?[-#/*\\\w]+(\[[0-9a-z_-]+\])?)\s*/;
var COLON_REGEX = /^:\s*/;
var VALUE_REGEX = /^((?:'(?:\\'|.)*?'|"(?:\\"|.)*?"|\([^)]*?\)|[^};])+)/;
var SEMICOLON_REGEX = /^[;\s]*/;
var TRIM_REGEX = /^\s+|\s+$/g;
var NEWLINE = "\n";
var FORWARD_SLASH = "/";
var ASTERISK = "*";
var EMPTY_STRING = "";
var TYPE_COMMENT = "comment";
var TYPE_DECLARATION = "declaration";
function index(style, options) {
  if (typeof style !== "string") {
    throw new TypeError("First argument must be a string");
  }
  if (!style) return [];
  options = options || {};
  var lineno = 1;
  var column = 1;
  function updatePosition(str) {
    var lines = str.match(NEWLINE_REGEX);
    if (lines) lineno += lines.length;
    var i = str.lastIndexOf(NEWLINE);
    column = ~i ? str.length - i : column + str.length;
  }
  function position() {
    var start = { line: lineno, column };
    return function(node) {
      node.position = new Position(start);
      whitespace();
      return node;
    };
  }
  function Position(start) {
    this.start = start;
    this.end = { line: lineno, column };
    this.source = options.source;
  }
  Position.prototype.content = style;
  function error(msg) {
    var err = new Error(
      options.source + ":" + lineno + ":" + column + ": " + msg
    );
    err.reason = msg;
    err.filename = options.source;
    err.line = lineno;
    err.column = column;
    err.source = style;
    if (options.silent) ;
    else {
      throw err;
    }
  }
  function match(re) {
    var m = re.exec(style);
    if (!m) return;
    var str = m[0];
    updatePosition(str);
    style = style.slice(str.length);
    return m;
  }
  function whitespace() {
    match(WHITESPACE_REGEX);
  }
  function comments(rules) {
    var c;
    rules = rules || [];
    while (c = comment()) {
      if (c !== false) {
        rules.push(c);
      }
    }
    return rules;
  }
  function comment() {
    var pos = position();
    if (FORWARD_SLASH != style.charAt(0) || ASTERISK != style.charAt(1)) return;
    var i = 2;
    while (EMPTY_STRING != style.charAt(i) && (ASTERISK != style.charAt(i) || FORWARD_SLASH != style.charAt(i + 1))) {
      ++i;
    }
    i += 2;
    if (EMPTY_STRING === style.charAt(i - 1)) {
      return error("End of comment missing");
    }
    var str = style.slice(2, i - 2);
    column += 2;
    updatePosition(str);
    style = style.slice(i);
    column += 2;
    return pos({
      type: TYPE_COMMENT,
      comment: str
    });
  }
  function declaration() {
    var pos = position();
    var prop = match(PROPERTY_REGEX);
    if (!prop) return;
    comment();
    if (!match(COLON_REGEX)) return error("property missing ':'");
    var val = match(VALUE_REGEX);
    var ret = pos({
      type: TYPE_DECLARATION,
      property: trim(prop[0].replace(COMMENT_REGEX, EMPTY_STRING)),
      value: val ? trim(val[0].replace(COMMENT_REGEX, EMPTY_STRING)) : EMPTY_STRING
    });
    match(SEMICOLON_REGEX);
    return ret;
  }
  function declarations() {
    var decls = [];
    comments(decls);
    var decl;
    while (decl = declaration()) {
      if (decl !== false) {
        decls.push(decl);
        comments(decls);
      }
    }
    return decls;
  }
  whitespace();
  return declarations();
}
function trim(str) {
  return str ? str.replace(TRIM_REGEX, EMPTY_STRING) : EMPTY_STRING;
}
function StyleToObject(style, iterator) {
  let styleObject = null;
  if (!style || typeof style !== "string") {
    return styleObject;
  }
  const declarations = index(style);
  const hasIterator = typeof iterator === "function";
  declarations.forEach((declaration) => {
    if (declaration.type !== "declaration") {
      return;
    }
    const { property, value } = declaration;
    if (hasIterator) {
      iterator(property, value, declaration);
    } else if (value) {
      styleObject = styleObject || {};
      styleObject[property] = value;
    }
  });
  return styleObject;
}
const NUMBER_CHAR_RE = /\d/;
const STR_SPLITTERS = ["-", "_", "/", "."];
function isUppercase(char = "") {
  if (NUMBER_CHAR_RE.test(char))
    return void 0;
  return char !== char.toLowerCase();
}
function splitByCase(str) {
  const parts = [];
  let buff = "";
  let previousUpper;
  let previousSplitter;
  for (const char of str) {
    const isSplitter = STR_SPLITTERS.includes(char);
    if (isSplitter === true) {
      parts.push(buff);
      buff = "";
      previousUpper = void 0;
      continue;
    }
    const isUpper = isUppercase(char);
    if (previousSplitter === false) {
      if (previousUpper === false && isUpper === true) {
        parts.push(buff);
        buff = char;
        previousUpper = isUpper;
        continue;
      }
      if (previousUpper === true && isUpper === false && buff.length > 1) {
        const lastChar = buff.at(-1);
        parts.push(buff.slice(0, Math.max(0, buff.length - 1)));
        buff = lastChar + char;
        previousUpper = isUpper;
        continue;
      }
    }
    buff += char;
    previousUpper = isUpper;
    previousSplitter = isSplitter;
  }
  parts.push(buff);
  return parts;
}
function pascalCase(str) {
  if (!str)
    return "";
  return splitByCase(str).map((p) => upperFirst(p)).join("");
}
function camelCase(str) {
  return lowerFirst(pascalCase(str || ""));
}
function upperFirst(str) {
  return str ? str[0].toUpperCase() + str.slice(1) : "";
}
function lowerFirst(str) {
  return str ? str[0].toLowerCase() + str.slice(1) : "";
}
function cssToStyleObj(css) {
  if (!css)
    return {};
  const styleObj = {};
  function iterator(name, value) {
    if (name.startsWith("-moz-") || name.startsWith("-webkit-") || name.startsWith("-ms-") || name.startsWith("-o-")) {
      styleObj[pascalCase(name)] = value;
      return;
    }
    if (name.startsWith("--")) {
      styleObj[name] = value;
      return;
    }
    styleObj[camelCase(name)] = value;
  }
  StyleToObject(css, iterator);
  return styleObj;
}
function executeCallbacks(...callbacks) {
  return (...args) => {
    for (const callback of callbacks) {
      if (typeof callback === "function") {
        callback(...args);
      }
    }
  };
}
function createParser(matcher, replacer) {
  const regex = RegExp(matcher, "g");
  return (str) => {
    if (typeof str !== "string") {
      throw new TypeError(`expected an argument of type string, but got ${typeof str}`);
    }
    if (!str.match(regex))
      return str;
    return str.replace(regex, replacer);
  };
}
const camelToKebab = createParser(/[A-Z]/, (match) => `-${match.toLowerCase()}`);
function styleToCSS(styleObj) {
  if (!styleObj || typeof styleObj !== "object" || Array.isArray(styleObj)) {
    throw new TypeError(`expected an argument of type object, but got ${typeof styleObj}`);
  }
  return Object.keys(styleObj).map((property) => `${camelToKebab(property)}: ${styleObj[property]};`).join("\n");
}
function styleToString(style = {}) {
  return styleToCSS(style).replace("\n", " ");
}
const EVENT_LIST = [
  "onabort",
  "onanimationcancel",
  "onanimationend",
  "onanimationiteration",
  "onanimationstart",
  "onauxclick",
  "onbeforeinput",
  "onbeforetoggle",
  "onblur",
  "oncancel",
  "oncanplay",
  "oncanplaythrough",
  "onchange",
  "onclick",
  "onclose",
  "oncompositionend",
  "oncompositionstart",
  "oncompositionupdate",
  "oncontextlost",
  "oncontextmenu",
  "oncontextrestored",
  "oncopy",
  "oncuechange",
  "oncut",
  "ondblclick",
  "ondrag",
  "ondragend",
  "ondragenter",
  "ondragleave",
  "ondragover",
  "ondragstart",
  "ondrop",
  "ondurationchange",
  "onemptied",
  "onended",
  "onerror",
  "onfocus",
  "onfocusin",
  "onfocusout",
  "onformdata",
  "ongotpointercapture",
  "oninput",
  "oninvalid",
  "onkeydown",
  "onkeypress",
  "onkeyup",
  "onload",
  "onloadeddata",
  "onloadedmetadata",
  "onloadstart",
  "onlostpointercapture",
  "onmousedown",
  "onmouseenter",
  "onmouseleave",
  "onmousemove",
  "onmouseout",
  "onmouseover",
  "onmouseup",
  "onpaste",
  "onpause",
  "onplay",
  "onplaying",
  "onpointercancel",
  "onpointerdown",
  "onpointerenter",
  "onpointerleave",
  "onpointermove",
  "onpointerout",
  "onpointerover",
  "onpointerup",
  "onprogress",
  "onratechange",
  "onreset",
  "onresize",
  "onscroll",
  "onscrollend",
  "onsecuritypolicyviolation",
  "onseeked",
  "onseeking",
  "onselect",
  "onselectionchange",
  "onselectstart",
  "onslotchange",
  "onstalled",
  "onsubmit",
  "onsuspend",
  "ontimeupdate",
  "ontoggle",
  "ontouchcancel",
  "ontouchend",
  "ontouchmove",
  "ontouchstart",
  "ontransitioncancel",
  "ontransitionend",
  "ontransitionrun",
  "ontransitionstart",
  "onvolumechange",
  "onwaiting",
  "onwebkitanimationend",
  "onwebkitanimationiteration",
  "onwebkitanimationstart",
  "onwebkittransitionend",
  "onwheel"
];
const EVENT_LIST_SET = new Set(EVENT_LIST);
function isEventHandler(key) {
  return EVENT_LIST_SET.has(key);
}
function mergeProps(...args) {
  const result = { ...args[0] };
  for (let i = 1; i < args.length; i++) {
    const props = args[i];
    if (!props)
      continue;
    for (const key of Object.keys(props)) {
      const a = result[key];
      const b = props[key];
      const aIsFunction = typeof a === "function";
      const bIsFunction = typeof b === "function";
      if (aIsFunction && typeof bIsFunction && isEventHandler(key)) {
        const aHandler = a;
        const bHandler = b;
        result[key] = composeHandlers(aHandler, bHandler);
      } else if (aIsFunction && bIsFunction) {
        result[key] = executeCallbacks(a, b);
      } else if (key === "class") {
        const aIsClassValue = isClassValue(a);
        const bIsClassValue = isClassValue(b);
        if (aIsClassValue && bIsClassValue) {
          result[key] = clsx(a, b);
        } else if (aIsClassValue) {
          result[key] = clsx(a);
        } else if (bIsClassValue) {
          result[key] = clsx(b);
        }
      } else if (key === "style") {
        const aIsObject = typeof a === "object";
        const bIsObject = typeof b === "object";
        const aIsString = typeof a === "string";
        const bIsString = typeof b === "string";
        if (aIsObject && bIsObject) {
          result[key] = { ...a, ...b };
        } else if (aIsObject && bIsString) {
          const parsedStyle = cssToStyleObj(b);
          result[key] = { ...a, ...parsedStyle };
        } else if (aIsString && bIsObject) {
          const parsedStyle = cssToStyleObj(a);
          result[key] = { ...parsedStyle, ...b };
        } else if (aIsString && bIsString) {
          const parsedStyleA = cssToStyleObj(a);
          const parsedStyleB = cssToStyleObj(b);
          result[key] = { ...parsedStyleA, ...parsedStyleB };
        } else if (aIsObject) {
          result[key] = a;
        } else if (bIsObject) {
          result[key] = b;
        } else if (aIsString) {
          result[key] = a;
        } else if (bIsString) {
          result[key] = b;
        }
      } else {
        result[key] = b !== void 0 ? b : a;
      }
    }
    for (const key of Object.getOwnPropertySymbols(props)) {
      const a = result[key];
      const b = props[key];
      result[key] = b !== void 0 ? b : a;
    }
  }
  if (typeof result.style === "object") {
    result.style = styleToString(result.style).replaceAll("\n", " ");
  }
  if (result.hidden === false) {
    result.hidden = void 0;
    delete result.hidden;
  }
  if (result.disabled === false) {
    result.disabled = void 0;
    delete result.disabled;
  }
  return result;
}
function createSubscriber(_) {
  return () => {
  };
}
const defaultWindow = void 0;
function getActiveElement(document2) {
  let activeElement = document2.activeElement;
  while (activeElement?.shadowRoot) {
    const node = activeElement.shadowRoot.activeElement;
    if (node === activeElement)
      break;
    else
      activeElement = node;
  }
  return activeElement;
}
class ActiveElement {
  #document;
  #subscribe;
  constructor(options = {}) {
    const { window = defaultWindow, document: document2 = window?.document } = options;
    if (window === void 0) return;
    this.#document = document2;
    this.#subscribe = createSubscriber();
  }
  get current() {
    this.#subscribe?.();
    if (!this.#document) return null;
    return getActiveElement(this.#document);
  }
}
new ActiveElement();
class Context {
  #name;
  #key;
  /**
   * @param name The name of the context.
   * This is used for generating the context key and error messages.
   */
  constructor(name) {
    this.#name = name;
    this.#key = Symbol(name);
  }
  /**
   * The key used to get and set the context.
   *
   * It is not recommended to use this value directly.
   * Instead, use the methods provided by this class.
   */
  get key() {
    return this.#key;
  }
  /**
   * Checks whether this has been set in the context of a parent component.
   *
   * Must be called during component initialisation.
   */
  exists() {
    return hasContext(this.#key);
  }
  /**
   * Retrieves the context that belongs to the closest parent component.
   *
   * Must be called during component initialisation.
   *
   * @throws An error if the context does not exist.
   */
  get() {
    const context = getContext(this.#key);
    if (context === void 0) {
      throw new Error(`Context "${this.#name}" not found`);
    }
    return context;
  }
  /**
   * Retrieves the context that belongs to the closest parent component,
   * or the given fallback value if the context does not exist.
   *
   * Must be called during component initialisation.
   */
  getOr(fallback) {
    const context = getContext(this.#key);
    if (context === void 0) {
      return fallback;
    }
    return context;
  }
  /**
   * Associates the given value with the current component and returns it.
   *
   * Must be called during component initialisation.
   */
  set(context) {
    return setContext(this.#key, context);
  }
}
class HPDAttrs {
  #variant;
  #prefix;
  attrs;
  constructor(config) {
    this.#variant = config.getVariant ? config.getVariant() : null;
    this.#prefix = this.#variant ? `data-${this.#variant}-` : `data-${config.component}-`;
    this.getAttr = this.getAttr.bind(this);
    this.selector = this.selector.bind(this);
    this.attrs = Object.fromEntries(config.parts.map((part) => [part, this.getAttr(part)]));
  }
  getAttr(part, variantOverride) {
    if (variantOverride)
      return `data-${variantOverride}-${part}`;
    return `${this.#prefix}${part}`;
  }
  selector(part, variantOverride) {
    return `[${this.getAttr(part, variantOverride)}]`;
  }
}
function createHPDAttrs(config) {
  const hpdAttrs = new HPDAttrs(config);
  return {
    ...hpdAttrs.attrs,
    selector: hpdAttrs.selector,
    getAttr: hpdAttrs.getAttr
  };
}
class RunConfigState {
  // Mutable slices — $state fields
  #modelTransport = void 0;
  #clients = void 0;
  #providerKey = void 0;
  #modelId = void 0;
  #apiKey = void 0;
  #providerEndpoint = void 0;
  #customHeaders = void 0;
  #providerOptions = void 0;
  #systemInstructions = void 0;
  #additionalSystemInstructions = void 0;
  #temperature = void 0;
  #maxOutputTokens = void 0;
  #topP = void 0;
  #topK = void 0;
  #frequencyPenalty = void 0;
  #presencePenalty = void 0;
  #chatModelId = void 0;
  #stopSequences = void 0;
  #chatAdditionalProperties = void 0;
  #reasoning = void 0;
  #permissionOverrides = {};
  #contextOverrides = void 0;
  #useCache = void 0;
  #coalesceDeltas = void 0;
  #skipTools = void 0;
  #runTimeout = void 0;
  #conversationIdOverride = void 0;
  #allowBackgroundResponses = void 0;
  #backgroundPollingInterval = void 0;
  #backgroundTimeout = void 0;
  #userMessage = void 0;
  #uploadStrategy = void 0;
  #audio = void 0;
  #triggerCompaction = void 0;
  #skipCompaction = void 0;
  #compactionBehaviorOverride = void 0;
  #structuredOutput = void 0;
  #clientToolInput = void 0;
  // Plain getters — reactive because they read $state fields
  get modelTransport() {
    return this.#modelTransport;
  }
  get clients() {
    return this.#clients;
  }
  get providerKey() {
    return this.#providerKey;
  }
  get modelId() {
    return this.#modelId;
  }
  get apiKey() {
    return this.#apiKey;
  }
  get providerEndpoint() {
    return this.#providerEndpoint;
  }
  get customHeaders() {
    return this.#customHeaders;
  }
  get providerOptions() {
    return this.#providerOptions;
  }
  get systemInstructions() {
    return this.#systemInstructions;
  }
  get temperature() {
    return this.#temperature;
  }
  get maxOutputTokens() {
    return this.#maxOutputTokens;
  }
  get topP() {
    return this.#topP;
  }
  get topK() {
    return this.#topK;
  }
  get frequencyPenalty() {
    return this.#frequencyPenalty;
  }
  get presencePenalty() {
    return this.#presencePenalty;
  }
  get chatModelId() {
    return this.#chatModelId;
  }
  get stopSequences() {
    return this.#stopSequences;
  }
  get chatAdditionalProperties() {
    return this.#chatAdditionalProperties;
  }
  get reasoning() {
    return this.#reasoning;
  }
  get additionalSystemInstructions() {
    return this.#additionalSystemInstructions;
  }
  get contextOverrides() {
    return this.#contextOverrides;
  }
  get useCache() {
    return this.#useCache;
  }
  get coalesceDeltas() {
    return this.#coalesceDeltas;
  }
  get skipTools() {
    return this.#skipTools;
  }
  get runTimeout() {
    return this.#runTimeout;
  }
  get conversationIdOverride() {
    return this.#conversationIdOverride;
  }
  get allowBackgroundResponses() {
    return this.#allowBackgroundResponses;
  }
  get backgroundPollingInterval() {
    return this.#backgroundPollingInterval;
  }
  get backgroundTimeout() {
    return this.#backgroundTimeout;
  }
  get userMessage() {
    return this.#userMessage;
  }
  get uploadStrategy() {
    return this.#uploadStrategy;
  }
  get audio() {
    return this.#audio;
  }
  get triggerCompaction() {
    return this.#triggerCompaction;
  }
  get skipCompaction() {
    return this.#skipCompaction;
  }
  get compactionBehaviorOverride() {
    return this.#compactionBehaviorOverride;
  }
  get structuredOutput() {
    return this.#structuredOutput;
  }
  get clientToolInput() {
    return this.#clientToolInput;
  }
  get permissionOverrides() {
    return this.#permissionOverrides;
  }
  // Collapses chat sub-object — undefined when all chat fields are unset
  get chat() {
    const {
      temperature,
      maxOutputTokens,
      topP,
      topK,
      frequencyPenalty,
      presencePenalty,
      chatModelId,
      stopSequences,
      chatAdditionalProperties,
      reasoning
    } = this;
    if (temperature === void 0 && maxOutputTokens === void 0 && topP === void 0 && topK === void 0 && frequencyPenalty === void 0 && presencePenalty === void 0 && chatModelId === void 0 && stopSequences === void 0 && chatAdditionalProperties === void 0 && reasoning === void 0) return void 0;
    return {
      ...temperature !== void 0 && { temperature },
      ...maxOutputTokens !== void 0 && { maxOutputTokens },
      ...topP !== void 0 && { topP },
      ...topK !== void 0 && { topK },
      ...frequencyPenalty !== void 0 && { frequencyPenalty },
      ...presencePenalty !== void 0 && { presencePenalty },
      ...chatModelId !== void 0 && { modelId: chatModelId },
      ...stopSequences !== void 0 && { stopSequences },
      ...chatAdditionalProperties !== void 0 && { additionalProperties: chatAdditionalProperties },
      ...reasoning !== void 0 && { reasoning }
    };
  }
  // Final value handed to send() — undefined when nothing is set
  get value() {
    const {
      modelTransport,
      clients,
      providerKey,
      modelId,
      apiKey,
      providerEndpoint,
      customHeaders,
      providerOptions,
      systemInstructions,
      additionalSystemInstructions,
      chat,
      contextOverrides,
      useCache,
      coalesceDeltas,
      skipTools,
      runTimeout,
      conversationIdOverride,
      allowBackgroundResponses,
      backgroundPollingInterval,
      backgroundTimeout,
      userMessage,
      uploadStrategy,
      audio,
      triggerCompaction,
      skipCompaction,
      compactionBehaviorOverride,
      structuredOutput,
      clientToolInput
    } = this;
    const permissionOverrides = Object.keys(this.#permissionOverrides).length > 0 ? this.#permissionOverrides : void 0;
    if (modelTransport === void 0 && clients === void 0 && providerKey === void 0 && modelId === void 0 && apiKey === void 0 && providerEndpoint === void 0 && customHeaders === void 0 && providerOptions === void 0 && systemInstructions === void 0 && additionalSystemInstructions === void 0 && chat === void 0 && permissionOverrides === void 0 && contextOverrides === void 0 && useCache === void 0 && coalesceDeltas === void 0 && skipTools === void 0 && runTimeout === void 0 && conversationIdOverride === void 0 && allowBackgroundResponses === void 0 && backgroundPollingInterval === void 0 && backgroundTimeout === void 0 && userMessage === void 0 && uploadStrategy === void 0 && audio === void 0 && triggerCompaction === void 0 && skipCompaction === void 0 && compactionBehaviorOverride === void 0 && structuredOutput === void 0 && clientToolInput === void 0) return void 0;
    return {
      ...modelTransport !== void 0 && { modelTransport },
      ...clients !== void 0 && { clients },
      ...providerKey !== void 0 && { providerKey },
      ...modelId !== void 0 && { modelId },
      ...apiKey !== void 0 && { apiKey },
      ...providerEndpoint !== void 0 && { providerEndpoint },
      ...customHeaders !== void 0 && { customHeaders },
      ...providerOptions !== void 0 && { providerOptions },
      ...systemInstructions !== void 0 && { systemInstructions },
      ...additionalSystemInstructions !== void 0 && { additionalSystemInstructions },
      ...chat !== void 0 && { chat },
      ...permissionOverrides !== void 0 && { permissionOverrides },
      ...contextOverrides !== void 0 && { contextOverrides },
      ...useCache !== void 0 && { useCache },
      ...coalesceDeltas !== void 0 && { coalesceDeltas },
      ...skipTools !== void 0 && { skipTools },
      ...runTimeout !== void 0 && { runTimeout },
      ...conversationIdOverride !== void 0 && { conversationIdOverride },
      ...allowBackgroundResponses !== void 0 && { allowBackgroundResponses },
      ...backgroundPollingInterval !== void 0 && { backgroundPollingInterval },
      ...backgroundTimeout !== void 0 && { backgroundTimeout },
      ...userMessage !== void 0 && { userMessage },
      ...uploadStrategy !== void 0 && { uploadStrategy },
      ...audio !== void 0 && { audio },
      ...triggerCompaction !== void 0 && { triggerCompaction },
      ...skipCompaction !== void 0 && { skipCompaction },
      ...compactionBehaviorOverride !== void 0 && { compactionBehaviorOverride },
      ...structuredOutput !== void 0 && { structuredOutput },
      ...clientToolInput !== void 0 && { clientToolInput }
    };
  }
  // Setters
  setModel(providerKey, modelId) {
    this.#providerKey = providerKey;
    this.#modelId = modelId;
  }
  setModelTransport(value) {
    this.#modelTransport = value;
  }
  setClients(value) {
    this.#clients = value;
  }
  setApiKey(value) {
    this.#apiKey = value;
  }
  setProviderEndpoint(value) {
    this.#providerEndpoint = value;
  }
  setCustomHeaders(value) {
    this.#customHeaders = value;
  }
  setProviderOptions(value) {
    this.#providerOptions = value;
  }
  setSystemInstructions(value) {
    this.#systemInstructions = value;
  }
  setTemperature(value) {
    this.#temperature = value;
  }
  setMaxTokens(value) {
    this.#maxOutputTokens = value;
  }
  setTopP(value) {
    this.#topP = value;
  }
  setTopK(value) {
    this.#topK = value;
  }
  setFrequencyPenalty(value) {
    this.#frequencyPenalty = value;
  }
  setPresencePenalty(value) {
    this.#presencePenalty = value;
  }
  setChatModelId(value) {
    this.#chatModelId = value;
  }
  setStopSequences(value) {
    this.#stopSequences = value;
  }
  setChatAdditionalProperties(value) {
    this.#chatAdditionalProperties = value;
  }
  setReasoning(value) {
    this.#reasoning = value;
  }
  setAdditionalSystemInstructions(value) {
    this.#additionalSystemInstructions = value;
  }
  setPermissionOverride(key, value) {
    if (value === void 0) {
      const { [key]: _, ...rest } = this.#permissionOverrides;
      this.#permissionOverrides = rest;
    } else {
      this.#permissionOverrides = { ...this.#permissionOverrides, [key]: value };
    }
  }
  setContextOverrides(value) {
    this.#contextOverrides = value;
  }
  setUseCache(value) {
    this.#useCache = value;
  }
  setCoalesceDeltas(value) {
    this.#coalesceDeltas = value;
  }
  setSkipTools(value) {
    this.#skipTools = value;
  }
  setRunTimeout(value) {
    this.#runTimeout = value;
  }
  setConversationIdOverride(value) {
    this.#conversationIdOverride = value;
  }
  setAllowBackgroundResponses(value) {
    this.#allowBackgroundResponses = value;
  }
  setBackgroundPollingInterval(value) {
    this.#backgroundPollingInterval = value;
  }
  setBackgroundTimeout(value) {
    this.#backgroundTimeout = value;
  }
  setUserMessage(value) {
    this.#userMessage = value;
  }
  setUploadStrategy(value) {
    this.#uploadStrategy = value;
  }
  setAudio(value) {
    this.#audio = value;
  }
  setTriggerCompaction(value) {
    this.#triggerCompaction = value;
  }
  setSkipCompaction(value) {
    this.#skipCompaction = value;
  }
  setCompactionBehaviorOverride(value) {
    this.#compactionBehaviorOverride = value;
  }
  setStructuredOutput(value) {
    this.#structuredOutput = value;
  }
  setClientToolInput(value) {
    this.#clientToolInput = value;
  }
  reset() {
    this.#modelTransport = void 0;
    this.#clients = void 0;
    this.#providerKey = void 0;
    this.#modelId = void 0;
    this.#apiKey = void 0;
    this.#providerEndpoint = void 0;
    this.#customHeaders = void 0;
    this.#providerOptions = void 0;
    this.#systemInstructions = void 0;
    this.#additionalSystemInstructions = void 0;
    this.#temperature = void 0;
    this.#maxOutputTokens = void 0;
    this.#topP = void 0;
    this.#topK = void 0;
    this.#frequencyPenalty = void 0;
    this.#presencePenalty = void 0;
    this.#chatModelId = void 0;
    this.#stopSequences = void 0;
    this.#chatAdditionalProperties = void 0;
    this.#reasoning = void 0;
    this.#permissionOverrides = {};
    this.#contextOverrides = void 0;
    this.#useCache = void 0;
    this.#coalesceDeltas = void 0;
    this.#skipTools = void 0;
    this.#runTimeout = void 0;
    this.#conversationIdOverride = void 0;
    this.#allowBackgroundResponses = void 0;
    this.#backgroundPollingInterval = void 0;
    this.#backgroundTimeout = void 0;
    this.#userMessage = void 0;
    this.#uploadStrategy = void 0;
    this.#audio = void 0;
    this.#triggerCompaction = void 0;
    this.#skipCompaction = void 0;
    this.#compactionBehaviorOverride = void 0;
    this.#structuredOutput = void 0;
    this.#clientToolInput = void 0;
  }
}
let counter = 0;
function createId(prefixOrUid, uid) {
  if (prefixOrUid !== void 0) {
    return `hpd-${prefixOrUid}`;
  }
  return `hpd-${++counter}`;
}
const kbd = {
  /** Enter key - commonly used for form submission */
  ENTER: "Enter"
};
function Input($$renderer, $$props) {
  $$renderer.component(($$renderer2) => {
    const uid = props_id($$renderer2);
    let {
      id = createId(uid),
      ref = null,
      value = void 0,
      defaultValue = "",
      onChange,
      onSubmit,
      disabled = false,
      placeholder = "Type a message...",
      autoFocus = false,
      autoResize = false,
      name,
      required = false,
      maxRows = 10,
      "aria-label": ariaLabel = "Message input",
      child,
      class: className,
      $$slots,
      $$events,
      ...restProps
    } = $$props;
    const isControlled = value !== void 0;
    let internalValue = defaultValue ?? "";
    const resolvedValue = derived(() => isControlled ? value ?? "" : internalValue);
    let rows = 1;
    let measurementClone = null;
    function updateRows(textarea) {
      if (resolvedValue() === "") {
        rows = 1;
        return;
      }
      if (!measurementClone) {
        measurementClone = textarea.cloneNode();
        measurementClone.setAttribute("aria-hidden", "true");
        measurementClone.removeAttribute("data-testid");
        measurementClone.removeAttribute("id");
        measurementClone.removeAttribute("name");
        measurementClone.removeAttribute("form");
        document.body.appendChild(measurementClone);
      }
      const clone = measurementClone;
      const computedStyle = getComputedStyle(textarea);
      clone.style.cssText = `
			position: absolute !important;
			visibility: hidden !important;
			pointer-events: none !important;
			top: -9999px !important;
			left: -9999px !important;
			width: ${textarea.clientWidth}px !important;
			height: auto !important;
			font: ${computedStyle.font} !important;
			font-family: ${computedStyle.fontFamily} !important;
			font-size: ${computedStyle.fontSize} !important;
			font-weight: ${computedStyle.fontWeight} !important;
			line-height: ${computedStyle.lineHeight} !important;
			letter-spacing: ${computedStyle.letterSpacing} !important;
			padding: ${computedStyle.padding} !important;
			border: ${computedStyle.border} !important;
			box-sizing: ${computedStyle.boxSizing} !important;
			white-space: ${computedStyle.whiteSpace} !important;
			overflow-wrap: ${computedStyle.overflowWrap} !important;
		`;
      clone.rows = 1;
      clone.value = resolvedValue();
      let lineHeight = parseFloat(computedStyle.lineHeight);
      if (!isFinite(lineHeight)) {
        const fontSize = parseFloat(computedStyle.fontSize);
        lineHeight = fontSize * 1.2;
      }
      const paddingTop = parseFloat(computedStyle.paddingTop) || 0;
      const paddingBottom = parseFloat(computedStyle.paddingBottom) || 0;
      const contentHeight = clone.scrollHeight - paddingTop - paddingBottom;
      const requiredRows = Math.max(1, Math.ceil(contentHeight / lineHeight));
      const newRows = Math.min(Math.max(1, requiredRows), maxRows);
      if (!isNaN(newRows) && newRows !== rows) {
        rows = newRows;
      }
    }
    function handleInput(event) {
      const textarea = event.currentTarget;
      const newValue = textarea.value;
      if (isControlled) {
        value = newValue;
      } else {
        internalValue = newValue;
      }
      updateRows(textarea);
      onChange?.({ reason: "input-change", event, value: newValue });
    }
    function handleKeyDown(event) {
      if (event.key === kbd.ENTER && !event.shiftKey && !event.isComposing) {
        event.preventDefault();
        const trimmedValue = resolvedValue().trim();
        if (trimmedValue && onSubmit) {
          onSubmit({ value: trimmedValue, event });
        }
      }
    }
    let focused = false;
    function handleFocus() {
      focused = true;
    }
    function handleBlur() {
      focused = false;
    }
    const mergedProps = derived(() => mergeProps(restProps, {
      id,
      role: "textbox",
      "aria-label": ariaLabel,
      "aria-multiline": "true",
      "aria-disabled": disabled,
      "data-input": "",
      "data-disabled": disabled ? "" : void 0,
      "data-filled": resolvedValue().length > 0 ? "" : void 0,
      "data-focused": focused ? "" : void 0,
      "data-rows": autoResize ? rows.toString() : void 0,
      class: className,
      disabled,
      placeholder,
      autofocus: autoFocus,
      rows: autoResize ? rows : void 0,
      name,
      required,
      value: resolvedValue(),
      oninput: handleInput,
      onfocus: handleFocus,
      onblur: handleBlur,
      onkeydown: handleKeyDown
    }));
    if (child) {
      $$renderer2.push("<!--[0-->");
      child($$renderer2, { props: mergedProps() });
      $$renderer2.push(`<!---->`);
    } else {
      $$renderer2.push("<!--[-1-->");
      $$renderer2.push(`<textarea${attributes({ ...mergedProps() })}></textarea>`);
    }
    $$renderer2.push(`<!--]-->`);
    bind_props($$props, { ref, value });
  });
}
const chatInputAttrs = createHPDAttrs({
  component: "chat-input",
  parts: ["root", "top", "leading", "input", "trailing", "bottom"]
});
const ChatInputRootContext = new Context("ChatInput.Root");
class ChatInputRootState {
  static create(opts) {
    return ChatInputRootContext.set(new ChatInputRootState(opts));
  }
  static get() {
    return ChatInputRootContext.get();
  }
  opts;
  // Internal state
  #internalValue = "";
  #focused = false;
  // Whether value is controlled by parent
  #isControlled = derived(() => this.opts.value?.current !== void 0);
  #value = derived(
    // Current value (controlled or uncontrolled)
    () => {
      if (this.#isControlled()) {
        const val = this.opts.value.current;
        return typeof val === "string" ? val : "";
      }
      return this.#internalValue;
    }
  );
  get value() {
    return this.#value();
  }
  set value($$value) {
    return this.#value($$value);
  }
  #disabled = derived(() => this.opts.disabled?.current ?? false);
  get disabled() {
    return this.#disabled();
  }
  set disabled($$value) {
    return this.#disabled($$value);
  }
  #_focused = derived(() => this.#focused);
  get focused() {
    return this.#_focused();
  }
  set focused($$value) {
    return this.#_focused($$value);
  }
  #characterCount = derived(() => this.value.length);
  get characterCount() {
    return this.#characterCount();
  }
  set characterCount($$value) {
    return this.#characterCount($$value);
  }
  #isEmpty = derived(() => this.value.trim() === "");
  get isEmpty() {
    return this.#isEmpty();
  }
  set isEmpty($$value) {
    return this.#isEmpty($$value);
  }
  #canSubmit = derived(() => !this.isEmpty && !this.disabled);
  get canSubmit() {
    return this.#canSubmit();
  }
  set canSubmit($$value) {
    return this.#canSubmit($$value);
  }
  constructor(opts) {
    this.opts = opts;
    const defaultValue = opts.defaultValue?.current ?? "";
    this.#internalValue = defaultValue;
  }
  // Update value (for uncontrolled mode)
  updateValue(newValue, reason = "user") {
    if (!this.#isControlled()) {
      this.#internalValue = newValue;
    }
    const onChange = this.opts.onChange?.current;
    if (onChange) {
      onChange(newValue);
    }
  }
  // Submit handler
  submit() {
    if (!this.canSubmit) return;
    const onSubmit = this.opts.onSubmit?.current;
    if (onSubmit) {
      onSubmit({ value: this.value });
    }
  }
  // Clear input
  clear() {
    this.updateValue("", "programmatic");
  }
  // Focus management
  setFocused(focused) {
    this.#focused = focused;
  }
  // Get HPD attribute for a part
  getHPDAttr = (part) => {
    return chatInputAttrs.getAttr(part);
  };
  #sharedProps = derived(
    // Shared props for all child components
    () => ({
      "data-disabled": this.disabled ? "" : void 0,
      "data-focused": this.focused ? "" : void 0,
      "data-empty": this.isEmpty ? "" : void 0
    })
  );
  get sharedProps() {
    return this.#sharedProps();
  }
  set sharedProps($$value) {
    return this.#sharedProps($$value);
  }
}
function Chat_input_root($$renderer, $$props) {
  $$renderer.component(($$renderer2) => {
    let {
      value = void 0,
      defaultValue,
      disabled = false,
      onSubmit,
      onChange,
      class: className,
      child,
      children,
      ref = null,
      $$slots,
      $$events,
      ...restProps
    } = $$props;
    const rootState = ChatInputRootState.create({
      value: boxWith(() => value),
      defaultValue: boxWith(() => defaultValue),
      disabled: boxWith(() => disabled),
      onSubmit: boxWith(() => onSubmit),
      onChange: boxWith(() => onChange)
    });
    const mergedProps = derived(() => mergeProps(restProps, { [rootState.getHPDAttr("root")]: "", ...rootState.sharedProps }, className ? { class: className } : {}));
    if (child) {
      $$renderer2.push("<!--[0-->");
      child($$renderer2, { props: mergedProps() });
      $$renderer2.push(`<!---->`);
    } else {
      $$renderer2.push("<!--[-1-->");
      $$renderer2.push(`<div${attributes({ ...mergedProps() })}>`);
      children?.($$renderer2);
      $$renderer2.push(`<!----></div>`);
    }
    $$renderer2.push(`<!--]-->`);
    bind_props($$props, { value, ref });
  });
}
function Chat_input_input($$renderer, $$props) {
  $$renderer.component(($$renderer2) => {
    let {
      placeholder = "Type a message...",
      maxRows = 5,
      minRows = 1,
      disabled,
      ref = null,
      child,
      children,
      class: className,
      $$slots,
      $$events,
      ...restProps
    } = $$props;
    const rootState = ChatInputRootState.get();
    function handleChange(details) {
      rootState.updateValue(details.value, "user");
    }
    function handleSubmit() {
      rootState.submit();
    }
    function handleFocus() {
      rootState.setFocused(true);
    }
    function handleBlur() {
      rootState.setFocused(false);
    }
    const resolvedDisabled = derived(() => disabled ?? rootState.disabled);
    const snippetProps = derived(() => ({
      value: rootState.value,
      focused: rootState.focused,
      disabled: resolvedDisabled(),
      isEmpty: rootState.isEmpty,
      characterCount: rootState.characterCount,
      canSubmit: rootState.canSubmit
    }));
    const controlledValue = derived(() => rootState.value);
    const mergedProps = derived(() => mergeProps(
      restProps,
      {
        [rootState.getHPDAttr("input")]: "",
        ...rootState.sharedProps
      },
      className ? { class: className } : {}
    ));
    let $$settled = true;
    let $$inner_renderer;
    function $$render_inner($$renderer3) {
      if (child) {
        $$renderer3.push("<!--[0-->");
        child($$renderer3, { ...snippetProps(), props: mergedProps() });
        $$renderer3.push(`<!---->`);
      } else if (children) {
        $$renderer3.push("<!--[1-->");
        $$renderer3.push(`<div${attributes({ ...mergedProps() })}>`);
        children($$renderer3, snippetProps());
        $$renderer3.push(`<!----></div>`);
      } else {
        $$renderer3.push("<!--[-1-->");
        Input($$renderer3, spread_props([
          {
            value: controlledValue(),
            placeholder,
            maxRows,
            disabled: resolvedDisabled(),
            onSubmit: handleSubmit,
            onChange: handleChange,
            onfocus: handleFocus,
            onblur: handleBlur
          },
          mergedProps(),
          {
            get ref() {
              return ref;
            },
            set ref($$value) {
              ref = $$value;
              $$settled = false;
            }
          }
        ]));
      }
      $$renderer3.push(`<!--]-->`);
    }
    do {
      $$settled = true;
      $$inner_renderer = $$renderer2.copy();
      $$render_inner($$inner_renderer);
    } while (!$$settled);
    $$renderer2.subsume($$inner_renderer);
    bind_props($$props, { ref });
  });
}
function Chat_input_bottom($$renderer, $$props) {
  $$renderer.component(($$renderer2) => {
    let {
      ref = null,
      child,
      children,
      class: className,
      $$slots,
      $$events,
      ...restProps
    } = $$props;
    const rootState = ChatInputRootState.get();
    const snippetProps = derived(() => ({
      value: rootState.value,
      focused: rootState.focused,
      disabled: rootState.disabled,
      isEmpty: rootState.isEmpty,
      characterCount: rootState.characterCount,
      canSubmit: rootState.canSubmit,
      submit: () => rootState.submit(),
      clear: () => rootState.clear()
    }));
    const mergedProps = derived(() => mergeProps(
      restProps,
      {
        [rootState.getHPDAttr("bottom")]: "",
        ...rootState.sharedProps
      },
      className ? { class: className } : {}
    ));
    if (child) {
      $$renderer2.push("<!--[0-->");
      child($$renderer2, { ...snippetProps(), props: mergedProps() });
      $$renderer2.push(`<!---->`);
    } else if (children) {
      $$renderer2.push("<!--[1-->");
      $$renderer2.push(`<div${attributes({ ...mergedProps() })}>`);
      children($$renderer2, snippetProps());
      $$renderer2.push(`<!----></div>`);
    } else {
      $$renderer2.push("<!--[-1-->");
      $$renderer2.push(`<div${attributes({ ...mergedProps() })}></div>`);
    }
    $$renderer2.push(`<!--]-->`);
    bind_props($$props, { ref });
  });
}
function Icon($$renderer, name, size = "md") {
  $$renderer.push(`<svg class="icon svelte-1uha8ag"${attr("data-size", size)} viewBox="0 0 24 24" aria-hidden="true">`);
  if (name === "arrow-up") {
    $$renderer.push("<!--[0-->");
    $$renderer.push(`<path d="M12 19V5" class="svelte-1uha8ag"></path><path d="m5 12 7-7 7 7" class="svelte-1uha8ag"></path>`);
  } else if (name === "check") {
    $$renderer.push("<!--[1-->");
    $$renderer.push(`<path d="m20 6-11 11-5-5" class="svelte-1uha8ag"></path>`);
  } else if (name === "chevron-down") {
    $$renderer.push("<!--[2-->");
    $$renderer.push(`<path d="m6 9 6 6 6-6" class="svelte-1uha8ag"></path>`);
  } else if (name === "copy") {
    $$renderer.push("<!--[3-->");
    $$renderer.push(`<rect x="9" y="9" width="13" height="13" rx="2" class="svelte-1uha8ag"></rect><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1" class="svelte-1uha8ag"></path>`);
  } else if (name === "cpu") {
    $$renderer.push("<!--[4-->");
    $$renderer.push(`<rect x="7" y="7" width="10" height="10" rx="2" class="svelte-1uha8ag"></rect><path d="M9 1v3" class="svelte-1uha8ag"></path><path d="M15 1v3" class="svelte-1uha8ag"></path><path d="M9 20v3" class="svelte-1uha8ag"></path><path d="M15 20v3" class="svelte-1uha8ag"></path><path d="M20 9h3" class="svelte-1uha8ag"></path><path d="M20 14h3" class="svelte-1uha8ag"></path><path d="M1 9h3" class="svelte-1uha8ag"></path><path d="M1 14h3" class="svelte-1uha8ag"></path>`);
  } else if (name === "edit") {
    $$renderer.push("<!--[5-->");
    $$renderer.push(`<path d="M12 20h9" class="svelte-1uha8ag"></path><path d="M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4Z" class="svelte-1uha8ag"></path>`);
  } else if (name === "folder") {
    $$renderer.push("<!--[6-->");
    $$renderer.push(`<path d="M3 7a2 2 0 0 1 2-2h5l2 2h7a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2Z" class="svelte-1uha8ag"></path>`);
  } else if (name === "folder-search") {
    $$renderer.push("<!--[7-->");
    $$renderer.push(`<path d="M3 7a2 2 0 0 1 2-2h5l2 2h7a2 2 0 0 1 2 2v4.5" class="svelte-1uha8ag"></path><path d="M3 9v8a2 2 0 0 0 2 2h8" class="svelte-1uha8ag"></path><circle cx="17" cy="17" r="3" class="svelte-1uha8ag"></circle><path d="m21 21-2-2" class="svelte-1uha8ag"></path>`);
  } else if (name === "folders") {
    $$renderer.push("<!--[8-->");
    $$renderer.push(`<path d="M3 7a2 2 0 0 1 2-2h4l2 2h6a2 2 0 0 1 2 2v1" class="svelte-1uha8ag"></path><path d="M5 11a2 2 0 0 1 2-2h4l2 2h6a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2Z" class="svelte-1uha8ag"></path>`);
  } else if (name === "hammer") {
    $$renderer.push("<!--[9-->");
    $$renderer.push(`<path d="m15 12-8.5 8.5a2.1 2.1 0 0 1-3-3L12 9" class="svelte-1uha8ag"></path><path d="m17.5 10.5 2-2a2.1 2.1 0 0 0 0-3l-1-1a2.1 2.1 0 0 0-3 0l-2 2" class="svelte-1uha8ag"></path><path d="m9 4 11 11" class="svelte-1uha8ag"></path>`);
  } else if (name === "loader-circle") {
    $$renderer.push("<!--[10-->");
    $$renderer.push(`<path d="M21 12a9 9 0 1 1-6.2-8.56" class="svelte-1uha8ag"></path>`);
  } else if (name === "message-square") {
    $$renderer.push("<!--[11-->");
    $$renderer.push(`<path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2Z" class="svelte-1uha8ag"></path>`);
  } else if (name === "panel-left-close") {
    $$renderer.push("<!--[12-->");
    $$renderer.push(`<rect x="3" y="4" width="18" height="16" rx="2" class="svelte-1uha8ag"></rect><path d="M9 4v16" class="svelte-1uha8ag"></path><path d="m16 10-2 2 2 2" class="svelte-1uha8ag"></path>`);
  } else if (name === "panel-left-open") {
    $$renderer.push("<!--[13-->");
    $$renderer.push(`<rect x="3" y="4" width="18" height="16" rx="2" class="svelte-1uha8ag"></rect><path d="M9 4v16" class="svelte-1uha8ag"></path><path d="m14 10 2 2-2 2" class="svelte-1uha8ag"></path>`);
  } else if (name === "plus") {
    $$renderer.push("<!--[14-->");
    $$renderer.push(`<path d="M5 12h14" class="svelte-1uha8ag"></path><path d="M12 5v14" class="svelte-1uha8ag"></path>`);
  } else if (name === "refresh") {
    $$renderer.push("<!--[15-->");
    $$renderer.push(`<path d="M21 12a9 9 0 0 1-15.5 6.2" class="svelte-1uha8ag"></path><path d="M3 12A9 9 0 0 1 18.5 5.8" class="svelte-1uha8ag"></path><path d="M18 2v4h-4" class="svelte-1uha8ag"></path><path d="M6 22v-4h4" class="svelte-1uha8ag"></path>`);
  } else if (name === "retry") {
    $$renderer.push("<!--[16-->");
    $$renderer.push(`<path d="M3 12a9 9 0 1 0 3-6.7" class="svelte-1uha8ag"></path><path d="M3 4v6h6" class="svelte-1uha8ag"></path>`);
  } else if (name === "settings") {
    $$renderer.push("<!--[17-->");
    $$renderer.push(`<path d="M12 15.5A3.5 3.5 0 1 0 12 8a3.5 3.5 0 0 0 0 7.5Z" class="svelte-1uha8ag"></path><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.9l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.9-.3 1.7 1.7 0 0 0-1 1.6V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1-1.6 1.7 1.7 0 0 0-1.9.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.9 1.7 1.7 0 0 0-1.6-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.6-1 1.7 1.7 0 0 0-.3-1.9l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.9.3h.1a1.7 1.7 0 0 0 1-1.6V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.6 1.7 1.7 0 0 0 1.9-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.9v.1a1.7 1.7 0 0 0 1.6 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1Z" class="svelte-1uha8ag"></path>`);
  } else if (name === "shield-alert") {
    $$renderer.push("<!--[18-->");
    $$renderer.push(`<path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10Z" class="svelte-1uha8ag"></path><path d="M12 8v5" class="svelte-1uha8ag"></path><path d="M12 17h.01" class="svelte-1uha8ag"></path>`);
  } else if (name === "sliders") {
    $$renderer.push("<!--[19-->");
    $$renderer.push(`<path d="M4 21v-7" class="svelte-1uha8ag"></path><path d="M4 10V3" class="svelte-1uha8ag"></path><path d="M12 21v-9" class="svelte-1uha8ag"></path><path d="M12 8V3" class="svelte-1uha8ag"></path><path d="M20 21v-5" class="svelte-1uha8ag"></path><path d="M20 12V3" class="svelte-1uha8ag"></path><path d="M2 14h4" class="svelte-1uha8ag"></path><path d="M10 8h4" class="svelte-1uha8ag"></path><path d="M18 16h4" class="svelte-1uha8ag"></path>`);
  } else if (name === "square") {
    $$renderer.push("<!--[20-->");
    $$renderer.push(`<rect x="6" y="6" width="12" height="12" rx="1" class="svelte-1uha8ag"></rect>`);
  } else if (name === "trash") {
    $$renderer.push("<!--[21-->");
    $$renderer.push(`<path d="M3 6h18" class="svelte-1uha8ag"></path><path d="M8 6V4h8v2" class="svelte-1uha8ag"></path><path d="M19 6 18 20H6L5 6" class="svelte-1uha8ag"></path><path d="M10 11v5" class="svelte-1uha8ag"></path><path d="M14 11v5" class="svelte-1uha8ag"></path>`);
  } else if (name === "x") {
    $$renderer.push("<!--[22-->");
    $$renderer.push(`<path d="M18 6 6 18" class="svelte-1uha8ag"></path><path d="m6 6 12 12" class="svelte-1uha8ag"></path>`);
  } else {
    $$renderer.push("<!--[-1-->");
  }
  $$renderer.push(`<!--]--></svg>`);
}
function _page($$renderer, $$props) {
  $$renderer.component(($$renderer2) => {
    const runConfig = new RunConfigState();
    let workspace = null;
    let workspaceProfiles = [];
    let activeWorkspaceKey = "";
    let sidebarCollapsed = false;
    let transcriptCollapsed = false;
    let composerValue = "";
    const activeSession = derived(() => {
      return null;
    });
    const activeSessionMatchesWorkspace = derived(() => {
      if (!activeSession()) return false;
      return true;
    });
    const messages = derived(() => activeSessionMatchesWorkspace() ? [] : []);
    const isStreaming = derived(() => activeSessionMatchesWorkspace() ? false : false);
    const activeTools = derived(() => activeSessionMatchesWorkspace() ? [] : []);
    const canSend = derived(() => Boolean(workspace));
    readStartupContext();
    const activeRootSummary = derived(() => {
      const roots = [];
      if (roots.length === 0) return "No roots";
      if (roots.length === 1) return roots[0].path;
      return `${roots[0].path} + ${roots.length - 1} more`;
    });
    const monitorStatus = derived(() => {
      return "Connecting";
    });
    const monitorItems = derived(() => buildMonitorItems());
    function resolveBackendUrl() {
      const origin = globalThis.location?.origin ?? "";
      if (origin.includes("localhost") || origin.includes("127.0.0.1")) return origin;
      return "http://127.0.0.1:4317";
    }
    resolveBackendUrl();
    function readStartupContext() {
      const params = new URLSearchParams(globalThis.location?.search ?? "");
      return {
        workspaceKey: params.get("workspace") ?? "",
        sessionId: params.get("session") ?? "",
        branchId: params.get("branch") ?? "main"
      };
    }
    async function sendMessage(value) {
      return;
    }
    function formatSessionLabel(session) {
      const name = typeof session.metadata?.name === "string" ? session.metadata.name : "";
      return name || session.id.slice(0, 16);
    }
    function buildMonitorItems() {
      {
        return [
          {
            id: "workspace-loading",
            label: "Workspace",
            detail: "Connecting to runtime",
            status: "active"
          }
        ];
      }
    }
    head("1uha8ag", $$renderer2, ($$renderer3) => {
      $$renderer3.title(($$renderer4) => {
        $$renderer4.push(`<title>HPD-OS Workspace</title>`);
      });
    });
    $$renderer2.push(`<div class="app-provider svelte-1uha8ag"><div${attr_class("workspace-shell svelte-1uha8ag", void 0, { "transcript-collapsed": transcriptCollapsed })}><aside${attr_class("sidebar svelte-1uha8ag", void 0, { "collapsed": sidebarCollapsed })}><section class="sidebar-section svelte-1uha8ag"><div class="section-header svelte-1uha8ag"><h1 class="svelte-1uha8ag">Workspaces</h1> <div class="icon-row svelte-1uha8ag"><button class="icon-button svelte-1uha8ag" type="button" aria-label="Add workspace" title="Add workspace">`);
    Icon($$renderer2, "plus");
    $$renderer2.push(`<!----></button> <button class="icon-button svelte-1uha8ag" type="button" aria-label="Refresh workspaces" title="Refresh workspaces">`);
    Icon($$renderer2, "refresh");
    $$renderer2.push(`<!----></button> <button class="icon-button svelte-1uha8ag" type="button"${attr("aria-label", "Collapse sidebar")}${attr("title", "Collapse sidebar")}>`);
    Icon($$renderer2, "panel-left-close");
    $$renderer2.push(`<!----></button></div></div> `);
    if (workspaceProfiles.length === 0) {
      $$renderer2.push("<!--[0-->");
      $$renderer2.push(`<div class="empty-chip svelte-1uha8ag">No workspaces configured.</div>`);
    } else {
      $$renderer2.push("<!--[-1-->");
      $$renderer2.push(`<div class="workspace-list svelte-1uha8ag"><!--[-->`);
      const each_array = ensure_array_like(workspaceProfiles);
      for (let $$index = 0, $$length = each_array.length; $$index < $$length; $$index++) {
        let profile = each_array[$$index];
        $$renderer2.push(`<div${attr_class("workspace-card svelte-1uha8ag", void 0, { "active": profile.key === activeWorkspaceKey })}${attr("title", profile.name)}><button type="button" class="workspace-select svelte-1uha8ag"><span class="workspace-icon svelte-1uha8ag">`);
        Icon($$renderer2, profile.roots && profile.roots.length > 1 ? "folders" : "folder", "sm");
        $$renderer2.push(`<!----></span> `);
        {
          $$renderer2.push("<!--[0-->");
          $$renderer2.push(`<span class="workspace-meta svelte-1uha8ag"><span class="workspace-name svelte-1uha8ag">${escape_html(profile.name)}</span> <span class="workspace-path svelte-1uha8ag">${escape_html(profile.roots?.[0]?.path ?? "No root")}</span></span>`);
        }
        $$renderer2.push(`<!--]--></button> `);
        {
          $$renderer2.push("<!--[0-->");
          $$renderer2.push(`<button class="mini-button svelte-1uha8ag" type="button" aria-label="Edit workspace">`);
          Icon($$renderer2, "edit", "xs");
          $$renderer2.push(`<!----></button> <button class="mini-button danger svelte-1uha8ag" type="button" aria-label="Delete workspace">`);
          Icon($$renderer2, "trash", "xs");
          $$renderer2.push(`<!----></button>`);
        }
        $$renderer2.push(`<!--]--></div>`);
      }
      $$renderer2.push(`<!--]--></div>`);
    }
    $$renderer2.push(`<!--]--></section> <section class="sidebar-section sessions-section svelte-1uha8ag"><div class="section-header svelte-1uha8ag"><h2 class="svelte-1uha8ag">${escape_html("Workspace Sessions")}</h2> <button class="icon-button svelte-1uha8ag" type="button" aria-label="Create new session" title="Create new session">`);
    Icon($$renderer2, "plus");
    $$renderer2.push(`<!----></button></div> `);
    {
      $$renderer2.push("<!--[-1-->");
      $$renderer2.push(`<div class="empty-chip svelte-1uha8ag">Connecting...</div>`);
    }
    $$renderer2.push(`<!--]--></section> `);
    {
      $$renderer2.push("<!--[0-->");
      $$renderer2.push(`<section class="sidebar-section all-sessions-section svelte-1uha8ag"><div class="section-header svelte-1uha8ag"><h2 class="svelte-1uha8ag">All Sessions</h2></div> `);
      {
        $$renderer2.push("<!--[-1-->");
      }
      $$renderer2.push(`<!--]--></section>`);
    }
    $$renderer2.push(`<!--]--> <section class="sidebar-footer svelte-1uha8ag"><button class="settings-button svelte-1uha8ag" type="button"><span class="settings-icon svelte-1uha8ag">`);
    Icon($$renderer2, "settings", "sm");
    $$renderer2.push(`<!----></span> `);
    {
      $$renderer2.push("<!--[0-->");
      $$renderer2.push(`<span class="svelte-1uha8ag"><strong class="svelte-1uha8ag">Settings</strong> <small class="svelte-1uha8ag">Providers, model, runtime</small></span>`);
    }
    $$renderer2.push(`<!--]--></button></section></aside> `);
    {
      $$renderer2.push("<!--[0-->");
      $$renderer2.push(`<main class="transcript-panel svelte-1uha8ag"><div class="messages-scroll svelte-1uha8ag">`);
      {
        $$renderer2.push("<!--[-1-->");
      }
      $$renderer2.push(`<!--]--> `);
      {
        $$renderer2.push("<!--[0-->");
        $$renderer2.push(`<div class="placeholder svelte-1uha8ag"><h2 class="svelte-1uha8ag">HPD-OS Workspace</h2> <p class="svelte-1uha8ag">Loading the Svelte workspace shell.</p></div>`);
      }
      $$renderer2.push(`<!--]--></div></main>`);
    }
    $$renderer2.push(`<!--]--> <aside class="workspace-rail svelte-1uha8ag"><div class="workspace-surface-shell svelte-1uha8ag"><div class="workspace-surface svelte-1uha8ag"><header class="svelte-1uha8ag"><div class="surface-title svelte-1uha8ag"><span class="surface-icon svelte-1uha8ag">`);
    Icon($$renderer2, "cpu");
    $$renderer2.push(`<!----></span> <span class="svelte-1uha8ag"><h2 class="svelte-1uha8ag">HPD-OS</h2> <p class="svelte-1uha8ag">${escape_html("No workspace selected")}</p></span></div> <span class="surface-status svelte-1uha8ag"${attr("data-active", isStreaming() || activeTools().length > 0)}>${escape_html(monitorStatus())}</span></header> <div class="surface-grid svelte-1uha8ag"><section class="svelte-1uha8ag"><span class="surface-label svelte-1uha8ag">Workspace Root</span> <code class="svelte-1uha8ag">${escape_html(activeRootSummary())}</code></section> <section class="svelte-1uha8ag"><span class="surface-label svelte-1uha8ag">Session</span> <code class="svelte-1uha8ag">${escape_html(activeSession() ? formatSessionLabel(activeSession()) : "No active session")}</code></section> <section class="svelte-1uha8ag"><span class="surface-label svelte-1uha8ag">Branch</span> <code class="svelte-1uha8ag">${escape_html("No branch")}</code></section> <section class="svelte-1uha8ag"><span class="surface-label svelte-1uha8ag">Model</span> <code class="svelte-1uha8ag">${escape_html(runConfig.modelId ?? "Default model")}</code></section></div> <div class="surface-activity svelte-1uha8ag"><div class="surface-label svelte-1uha8ag">Current Activity</div> `);
    if (monitorItems().length) {
      $$renderer2.push("<!--[0-->");
      $$renderer2.push(`<div class="surface-activity-list svelte-1uha8ag"><!--[-->`);
      const each_array_7 = ensure_array_like(monitorItems());
      for (let $$index_7 = 0, $$length = each_array_7.length; $$index_7 < $$length; $$index_7++) {
        let item = each_array_7[$$index_7];
        $$renderer2.push(`<div class="surface-activity-row svelte-1uha8ag"${attr("data-status", item.status)}><span class="monitor-dot svelte-1uha8ag"></span> <span class="svelte-1uha8ag"><strong class="svelte-1uha8ag">${escape_html(item.label)}</strong> <small class="svelte-1uha8ag">${escape_html(item.detail)}</small></span></div>`);
      }
      $$renderer2.push(`<!--]--></div>`);
    } else {
      $$renderer2.push("<!--[-1-->");
      $$renderer2.push(`<p class="svelte-1uha8ag">No activity yet.</p>`);
    }
    $$renderer2.push(`<!--]--></div></div></div> <footer class="composer-area svelte-1uha8ag"><div class="composer-card svelte-1uha8ag">`);
    {
      let children = function($$renderer3) {
        if (Chat_input_input) {
          $$renderer3.push("<!--[-->");
          Chat_input_input($$renderer3, {
            placeholder: "Type an instruction for the agent...",
            minRows: 2,
            maxRows: 6,
            class: "composer-input"
          });
          $$renderer3.push("<!--]-->");
        } else {
          $$renderer3.push("<!--[!-->");
          $$renderer3.push("<!--]-->");
        }
        $$renderer3.push(` <div class="composer-bottom svelte-1uha8ag">`);
        {
          let children2 = function($$renderer4, inputState) {
            $$renderer4.push(`<div class="model-strip svelte-1uha8ag"><span class="model-icon svelte-1uha8ag">`);
            Icon($$renderer4, "plus", "xs");
            $$renderer4.push(`<!----></span> <span class="svelte-1uha8ag">${escape_html(runConfig.modelId ?? "Default model")}</span> `);
            if (isStreaming()) {
              $$renderer4.push("<!--[0-->");
              $$renderer4.push(`<button type="button" class="stop-button svelte-1uha8ag">`);
              Icon($$renderer4, "square", "xs");
              $$renderer4.push(`<!----><span class="svelte-1uha8ag">Stop</span></button>`);
            } else {
              $$renderer4.push("<!--[-1-->");
            }
            $$renderer4.push(`<!--]--></div> <button class="send-button svelte-1uha8ag" type="button" aria-label="Send message"${attr("disabled", !inputState.canSubmit, true)}>`);
            Icon($$renderer4, "arrow-up", "sm");
            $$renderer4.push(`<!----></button>`);
          };
          if (Chat_input_bottom) {
            $$renderer3.push("<!--[-->");
            Chat_input_bottom($$renderer3, { children: children2, $$slots: { default: true } });
            $$renderer3.push("<!--]-->");
          } else {
            $$renderer3.push("<!--[!-->");
            $$renderer3.push("<!--]-->");
          }
        }
        $$renderer3.push(`</div>`);
      };
      if (Chat_input_root) {
        $$renderer2.push("<!--[-->");
        Chat_input_root($$renderer2, {
          value: composerValue,
          disabled: !canSend(),
          onChange: (value) => composerValue = value,
          onSubmit: (details) => {
            composerValue = "";
            void sendMessage(details.value);
          },
          children,
          $$slots: { default: true }
        });
        $$renderer2.push("<!--]-->");
      } else {
        $$renderer2.push("<!--[!-->");
        $$renderer2.push("<!--]-->");
      }
    }
    $$renderer2.push(`</div> <div class="monitor-card svelte-1uha8ag"><button class="icon-button monitor-toggle svelte-1uha8ag" type="button"${attr("aria-label", "Collapse transcript")}>`);
    Icon($$renderer2, "panel-left-close");
    $$renderer2.push(`<!----></button> <div class="monitor-content svelte-1uha8ag"><header class="monitor-header svelte-1uha8ag"><strong class="svelte-1uha8ag">${escape_html(monitorStatus())}</strong> <span class="svelte-1uha8ag">${escape_html(messages().length)} messages</span></header> <div class="monitor-feed svelte-1uha8ag" aria-live="polite"><!--[-->`);
    const each_array_8 = ensure_array_like(monitorItems());
    for (let $$index_8 = 0, $$length = each_array_8.length; $$index_8 < $$length; $$index_8++) {
      let item = each_array_8[$$index_8];
      $$renderer2.push(`<div class="monitor-row svelte-1uha8ag"${attr("data-status", item.status)}><span class="monitor-dot svelte-1uha8ag"></span> <span class="monitor-row-copy svelte-1uha8ag"><span class="svelte-1uha8ag">${escape_html(item.label)}</span> <small class="svelte-1uha8ag">${escape_html(item.detail)}</small></span></div>`);
    }
    $$renderer2.push(`<!--]--></div></div></div></footer></aside></div> `);
    {
      $$renderer2.push("<!--[-1-->");
    }
    $$renderer2.push(`<!--]--> `);
    {
      $$renderer2.push("<!--[-1-->");
    }
    $$renderer2.push(`<!--]--> `);
    {
      $$renderer2.push("<!--[-1-->");
    }
    $$renderer2.push(`<!--]--></div>`);
  });
}
export {
  _page as default
};
