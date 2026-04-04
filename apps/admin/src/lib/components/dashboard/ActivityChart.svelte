<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import { Chart, LineController, LineElement, PointElement, LinearScale, CategoryScale, Legend, Tooltip, Filler } from 'chart.js';

	Chart.register(LineController, LineElement, PointElement, LinearScale, CategoryScale, Legend, Tooltip, Filler);

	let { dataPoints }: {
		dataPoints: Array<{ time: Date; messagesPerMinute: number; activeConnections: number }>;
	} = $props();

	let canvas: HTMLCanvasElement;
	let chart: Chart | null = null;

	function formatTime(d: Date): string {
		return d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' });
	}

	onMount(() => {
		chart = new Chart(canvas, {
			type: 'line',
			data: {
				labels: dataPoints.map(p => formatTime(p.time)),
				datasets: [
					{
						label: 'Messages/min',
						data: dataPoints.map(p => p.messagesPerMinute),
						borderColor: '#f0b232',
						backgroundColor: 'rgba(240, 178, 50, 0.1)',
						borderWidth: 2,
						pointRadius: 0,
						tension: 0.3,
						fill: true,
						yAxisID: 'y'
					},
					{
						label: 'Connections',
						data: dataPoints.map(p => p.activeConnections),
						borderColor: '#43b581',
						backgroundColor: 'rgba(67, 181, 129, 0.1)',
						borderWidth: 2,
						pointRadius: 0,
						tension: 0.3,
						fill: true,
						yAxisID: 'y1'
					}
				]
			},
			options: {
				responsive: true,
				maintainAspectRatio: false,
				interaction: { mode: 'index', intersect: false },
				animation: false,
				scales: {
					x: {
						ticks: { color: '#888', maxTicksLimit: 10 },
						grid: { color: 'rgba(255,255,255,0.05)' }
					},
					y: {
						type: 'linear',
						position: 'left',
						beginAtZero: true,
						title: { display: true, text: 'Messages/min', color: '#888' },
						ticks: { color: '#888' },
						grid: { color: 'rgba(255,255,255,0.05)' }
					},
					y1: {
						type: 'linear',
						position: 'right',
						beginAtZero: true,
						title: { display: true, text: 'Connections', color: '#888' },
						ticks: { color: '#888' },
						grid: { drawOnChartArea: false }
					}
				},
				plugins: {
					legend: {
						labels: { color: '#fff', usePointStyle: true, pointStyle: 'line' }
					}
				}
			}
		});
	});

	$effect(() => {
		if (!chart) return;
		const labels = dataPoints.map(p => formatTime(p.time));
		const msgs = dataPoints.map(p => p.messagesPerMinute);
		const conns = dataPoints.map(p => p.activeConnections);

		chart.data.labels = labels;
		chart.data.datasets[0].data = msgs;
		chart.data.datasets[1].data = conns;
		chart.update('none');
	});

	onDestroy(() => {
		chart?.destroy();
		chart = null;
	});
</script>

<div class="chart-card">
	<h3>Live Activity</h3>
	<div class="chart-container">
		<canvas bind:this={canvas}></canvas>
	</div>
</div>

<style>
	.chart-card {
		background: var(--bg-secondary);
		border: 1px solid var(--border);
		border-radius: var(--radius);
		padding: 20px;
		margin-top: 24px;
	}

	h3 {
		margin: 0 0 16px;
		font-size: 16px;
	}

	.chart-container {
		position: relative;
		height: 300px;
	}
</style>
