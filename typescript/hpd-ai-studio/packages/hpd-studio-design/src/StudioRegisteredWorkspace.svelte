<script lang="ts">
  import StudioBoundedGrid from './StudioBoundedGrid.svelte';
  import StudioCommandWorkbench from './StudioCommandWorkbench.svelte';
  import StudioObservationState from './StudioObservationState.svelte';
  import StudioResourceLinks from './StudioResourceLinks.svelte';
  import type { StudioDisplayColumn, StudioDisplayObservation, StudioDisplayRow } from './types.ts';
  interface Resource { readonly kind: string; readonly authorityChecksum: string }
  interface Link { readonly relation: string; readonly label: string; readonly target: Resource }
  interface Section { readonly sectionId:string; readonly labelMessageId:string; readonly viewIds:readonly string[]; readonly commandIds:readonly string[] }
  interface View { readonly viewId:string; readonly presentation:Readonly<{grid:Readonly<Record<string,unknown>>|null;chart:Readonly<Record<string,unknown>>|null;emptyState:string}> }
  interface Page { readonly pageId:string; readonly presentation:Readonly<{workspace:string;sections:readonly Section[];resourceRail:Readonly<Record<string,unknown>>|null}>; readonly views:readonly View[] }
  interface Commands { open(id:string,target:never,input?:unknown):void;snapshot():unknown;subscribe(listener:(state:unknown)=>void):()=>void;
    preview(signal?:AbortSignal):Promise<void>;acknowledge(id:string,accepted:boolean):void;execute(signal?:AbortSignal):Promise<void>;resolve(signal?:AbortSignal):Promise<void>;close():void }
  let { eyebrow, page, resource, observation, views = {}, links = [], commands, onnavigate }:
    { eyebrow:string;page:Page;resource:Resource|null;observation:StudioDisplayObservation;views?:Readonly<Record<string,unknown>>;
      links?:readonly Link[];commands:Commands;onnavigate:(link:Link)=>void|Promise<void> } = $props();
  let selected = $state<string|null>(null);
  let workbenchOpen = $state(false);
  const title = $derived(page.pageId);
  const commandIds = $derived([...new Set(page.presentation.sections.flatMap(section => section.commandIds))].sort());
  function view(id:string):View|undefined{return page.views.find(candidate=>candidate.viewId===id)}
  function rows(id:string):readonly StudioDisplayRow[]{return projectRows(views[id],view(id)?.presentation.grid,id)}
  function columns(id:string):readonly StudioDisplayColumn[]{return projectColumns(view(id)?.presentation.grid)}
</script>

<main class="studio-workspace" data-workspace={page.presentation.workspace} data-workspace-state={observation.state}>
  <header class="studio-workspace-header"><div><p class="studio-label">{eyebrow}</p><h1>{title}</h1>
    <p class="studio-text-safe text-sm text-studio-muted">Registered {page.presentation.workspace} workspace with finite authorized evidence.</p></div>
    {#if commandIds.length > 0}<button class="studio-button" type="button" aria-expanded={workbenchOpen} aria-controls="studio-workbench"
      onclick={()=>workbenchOpen=!workbenchOpen}>Workbench</button>{/if}</header>
  <StudioObservationState {observation} title={title}>
    <div class="grid gap-5">
      {#each page.presentation.sections as section (section.sectionId)}
        <section class="studio-panel grid gap-3 p-4" aria-labelledby={`${page.pageId}-${section.sectionId}`}>
          <h2 id={`${page.pageId}-${section.sectionId}`} class="text-base font-bold">{section.labelMessageId}</h2>
          {#each section.viewIds as viewId (viewId)}
            {@const definition=view(viewId)}
            {#if definition}
              <section class="grid gap-2" aria-label={viewId}>
                <h3 class="studio-label">{viewId}</h3>
                {#if definition.presentation.chart}{@render StudioSafeChart(definition.presentation.chart, views[viewId])}{/if}
                {#if definition.presentation.grid}
                  <StudioBoundedGrid caption={viewId} columns={columns(viewId)} rows={rows(viewId)} selectedId={selected}
                    onselect={id=>selected=id}/>
                {:else if !definition.presentation.chart}{@render StudioDisclosureValue(views[viewId], definition.presentation.emptyState)}{/if}
              </section>
            {/if}
          {/each}
          {#each section.commandIds as commandId (commandId)}
            <button class="studio-button justify-self-start" type="button" disabled={resource===null} onclick={()=>{if(resource){commands.open(commandId,resource as never,Object.freeze({}));workbenchOpen=true}}}>
              Review {commandId}</button>
          {/each}
        </section>
      {/each}
      <StudioResourceLinks {links} {onnavigate}/>
    </div>
  </StudioObservationState>
  {#if commandIds.length>0}<aside id="studio-workbench" class:studio-workbench-open={workbenchOpen} class="studio-workbench" aria-label="Command and receipt workbench">
    <div class="flex items-center justify-between gap-3"><h2>Workbench</h2><button class="studio-button studio-button-sm" type="button" onclick={()=>workbenchOpen=false}>Close</button></div>
    <StudioCommandWorkbench {commandIds} target={resource} {commands}/></aside>{/if}
</main>

{#snippet StudioDisclosureValue(value:unknown,emptyState:string)}
  {#if value===undefined||value===null||(Array.isArray(value)&&value.length===0)}<div class="studio-empty"><strong>{emptyState}</strong><span>No disclosed value is present.</span></div>
  {:else if Array.isArray(value)}<ul class="studio-disclosure-values">{#each value.slice(0,500) as member,index (index)}<li>{display(member)}</li>{/each}</ul>
  {:else if isRecord(value)}<dl class="studio-disclosure-list">{#each Object.entries(value) as [key,member] (key)}<div><dt>{key}</dt><dd>{display(member)}</dd></div>{/each}</dl>
  {:else}<p class="studio-text-safe">{display(value)}</p>{/if}
{/snippet}
{#snippet StudioSafeChart(definition:Readonly<Record<string,unknown>>,value:unknown)}
  <figure class="studio-chart" aria-label={typeof definition.chartId==='string'?definition.chartId:'Registered aggregate chart'}>
    <figcaption class="studio-label">{typeof definition.chartId==='string'?definition.chartId:'Registered aggregate chart'}</figcaption>
    {@render StudioDisclosureValue(value, 'noItems')}
  </figure>
{/snippet}

<script module lang="ts">
  function isRecord(value:unknown):value is Record<string,unknown>{return value!==null&&typeof value==='object'&&!Array.isArray(value)}
  function display(value:unknown):string { if(value===null)return 'Null';if(value===undefined)return 'Unavailable';if(typeof value==='string'||typeof value==='number'||typeof value==='boolean')return String(value);
    if(Array.isArray(value))return `${value.length} disclosed items`;return 'Structured disclosed value'; }
  function items(value:unknown):readonly unknown[]{if(Array.isArray(value))return value;if(isRecord(value)&&Array.isArray(value.items))return value.items;return value===undefined||value===null?[]:[value]}
  function projectColumns(grid:Readonly<Record<string,unknown>>|null|undefined):readonly StudioDisplayColumn[]{
    const source=grid&&Array.isArray(grid.columns)?grid.columns:[];const registered=source.filter(isRecord).filter(column=>column.initiallyVisible!==false).map(column=>({
      id:String(column.stablePropertyOrEdgeId??column.columnId),label:String(column.labelMessageId??column.columnId),width:(Number(column.initialWidthCssPixels)>=360?'wide':Number(column.initialWidthCssPixels)<=180?'compact':'standard') as 'wide'|'compact'|'standard'}));
    return registered; }
  function projectRows(value:unknown,grid:Readonly<Record<string,unknown>>|null|undefined,viewId:string):readonly StudioDisplayRow[]{
    if(!grid)return[];const columns=projectColumns(grid);const maximumRows=Number(grid.maximumRows);
    if(!Number.isInteger(maximumRows)||maximumRows<1||columns.length===0)return[];
    return items(value).slice(0,maximumRows).map((item,index)=>{const record=isRecord(item)?item:{};const cells:Record<string,string>={};
      for(const column of columns)cells[column.id]=display(record[column.id]);return{id:`${viewId}:${index}`,label:`${viewId} ${index+1}`,cells};});}
</script>
