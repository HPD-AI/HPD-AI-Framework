import App from "./App.svelte";
import "./styles.css";
import { mount } from "svelte";

const root = document.getElementById("app");
if (!root) throw new Error("HPDOS app root is not mounted.");

mount(App, { target: root });
