//! The machine as its kernel counts it (D-100): per-cpu times, a few memory fields, the load and the CPU temperature, read from
//! the host's `/proc` and `/sys` and written in the node_exporter text format, limited to the families Portarr reads (D-72).
//! Raw cumulative counters, never rates: Portarr turns two readings into a load (D-30).

use std::fmt::Write as _;
use std::fs;
use std::io;
use std::path::Path;

/// The `/proc/meminfo` fields Portarr reads, in the order they are written (the same list as Portarr's node_exporter parser).
pub const MEMORY_FIELDS: [&str; 8] = ["MemTotal", "MemFree", "MemAvailable", "Buffers", "Cached", "SReclaimable", "SwapTotal", "SwapFree"];

/// hwmon chips that measure the CPU package or its cores (Intel, AMD, Raspberry Pi and other ARM boards); the same list as the sampler's.
const CPU_CHIPS: [&str; 5] = ["coretemp", "k10temp", "zenpower", "cpu_thermal", "cpu-thermal"];

/// Clock ticks per second of `/proc/stat` (USER_HZ), which node_exporter divides by too.
const TICKS_PER_SECOND: f64 = 100.0;

pub const CONTENT_TYPE: &str = "text/plain; version=0.0.4; charset=utf-8";

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CpuTicks {
    pub cpu: String,
    pub user: u64,
    pub nice: u64,
    pub system: u64,
    pub idle: u64,
    pub iowait: u64,
    pub irq: u64,
    pub softirq: u64,
    pub steal: u64,
}

#[derive(Debug, Clone, PartialEq)]
pub struct Temperature {
    pub chip: String,
    pub sensor: String,
    pub celsius: f64,
}

#[derive(Debug, Clone, PartialEq)]
pub struct Snapshot {
    pub cpus: Vec<CpuTicks>,
    /// Bytes, in the order of [`MEMORY_FIELDS`]; a field the kernel does not report is absent.
    pub memory: Vec<(&'static str, u64)>,
    pub load1: Option<f64>,
    pub temperatures: Vec<Temperature>,
}

#[derive(Debug)]
pub enum HostError {
    Io(io::Error),
    /// The files are readable but are not Linux's `/proc` (no cpu, no total memory).
    Invalid,
}

impl From<io::Error> for HostError {
    fn from(error: io::Error) -> HostError {
        HostError::Io(error)
    }
}

/// Reads the machine once. The load and the temperature are optional (a virtual machine has no sensor): only the cpu times and the
/// memory are required.
pub fn read_snapshot(proc_path: &str, sys_path: &str) -> Result<Snapshot, HostError> {
    let proc_dir = Path::new(proc_path);
    let cpus = parse_cpus(&fs::read_to_string(proc_dir.join("stat"))?);
    let memory = parse_memory(&fs::read_to_string(proc_dir.join("meminfo"))?);
    let has = |field: &str| memory.iter().any(|(name, _)| *name == field);
    if cpus.is_empty() || !has("MemTotal") || !has("MemAvailable") {
        return Err(HostError::Invalid);
    }

    let load1 = fs::read_to_string(proc_dir.join("loadavg"))
        .ok()
        .and_then(|text| text.split_whitespace().next().and_then(|value| value.parse().ok()));
    Ok(Snapshot {
        cpus,
        memory,
        load1,
        temperatures: read_temperatures(Path::new(sys_path)),
    })
}

/// The `cpuN` lines of `/proc/stat`. The aggregate `cpu` line is Portarr's to compute; a line of fewer than nine fields is skipped,
/// and a field that is not a number reads as zero.
pub fn parse_cpus(stat: &str) -> Vec<CpuTicks> {
    stat.lines()
        .filter(|line| line.starts_with("cpu") && line.as_bytes().get(3).is_some_and(u8::is_ascii_digit))
        .filter_map(|line| {
            let fields: Vec<&str> = line.split_whitespace().collect();
            if fields.len() < 9 {
                return None;
            }
            let field = |index: usize| fields[index].parse::<u64>().unwrap_or(0);
            Some(CpuTicks {
                cpu: fields[0][3..].to_string(),
                user: field(1),
                nice: field(2),
                system: field(3),
                idle: field(4),
                iowait: field(5),
                irq: field(6),
                softirq: field(7),
                steal: field(8),
            })
        })
        .collect()
}

/// The wanted fields of `/proc/meminfo`, in bytes (the file counts kB), in the order of [`MEMORY_FIELDS`].
pub fn parse_memory(meminfo: &str) -> Vec<(&'static str, u64)> {
    MEMORY_FIELDS
        .iter()
        .filter_map(|wanted| {
            meminfo.lines().find_map(|line| {
                let (name, rest) = line.split_once(':')?;
                if name != *wanted {
                    return None;
                }
                let mut parts = rest.split_whitespace();
                let value: u64 = parts.next()?.parse().ok()?;
                let bytes = if parts.next().is_some_and(|unit| unit.eq_ignore_ascii_case("kB")) {
                    value.saturating_mul(1024)
                } else {
                    value
                };
                Some((*wanted, bytes))
            })
        })
        .collect()
}

/// The sensors of the CPU chips, in a stable order; none when the machine has none, and a sensor that cannot be read is skipped.
fn read_temperatures(sys_dir: &Path) -> Vec<Temperature> {
    let mut chips: Vec<_> = match fs::read_dir(sys_dir.join("class").join("hwmon")) {
        Ok(entries) => entries.filter_map(Result::ok).map(|entry| entry.path()).collect(),
        Err(_) => return Vec::new(),
    };
    chips.sort();

    let mut sensors = Vec::new();
    for chip_dir in chips {
        let Ok(name) = fs::read_to_string(chip_dir.join("name")) else { continue };
        let chip = name.trim().to_string();
        let lower = chip.to_ascii_lowercase();
        if !CPU_CHIPS.iter().any(|cpu_chip| lower.contains(cpu_chip)) {
            continue;
        }

        let Ok(entries) = fs::read_dir(&chip_dir) else { continue };
        let mut inputs: Vec<String> = entries
            .filter_map(Result::ok)
            .filter_map(|entry| entry.file_name().into_string().ok())
            .filter(|file| file.starts_with("temp") && file.ends_with("_input"))
            .collect();
        inputs.sort();

        for input in inputs {
            let Some(milli) = fs::read_to_string(chip_dir.join(&input)).ok().and_then(|text| text.trim().parse::<f64>().ok()) else {
                continue;
            };
            let sensor = input.split('_').next().unwrap_or_default().to_string();
            sensors.push(Temperature {
                chip: chip.clone(),
                sensor,
                celsius: milli / 1000.0,
            });
        }
    }
    sensors
}

/// The snapshot in the node_exporter text exposition format (version 0.0.4), limited to what Portarr reads.
pub fn exposition(snapshot: &Snapshot) -> String {
    let mut text = String::with_capacity(4096);
    text.push_str("# HELP node_cpu_seconds_total Seconds the CPUs spent in each mode.\n# TYPE node_cpu_seconds_total counter\n");
    for cpu in &snapshot.cpus {
        for (mode, ticks) in [
            ("idle", cpu.idle),
            ("iowait", cpu.iowait),
            ("irq", cpu.irq),
            ("nice", cpu.nice),
            ("softirq", cpu.softirq),
            ("steal", cpu.steal),
            ("system", cpu.system),
            ("user", cpu.user),
        ] {
            let _ = writeln!(
                text,
                "node_cpu_seconds_total{{cpu=\"{}\",mode=\"{mode}\"}} {}",
                escape(&cpu.cpu),
                ticks as f64 / TICKS_PER_SECOND
            );
        }
    }

    for (field, bytes) in &snapshot.memory {
        let _ = write!(text, "# TYPE node_memory_{field}_bytes gauge\nnode_memory_{field}_bytes {bytes}\n");
    }

    if let Some(load) = snapshot.load1 {
        let _ = write!(text, "# TYPE node_load1 gauge\nnode_load1 {load}\n");
    }

    if !snapshot.temperatures.is_empty() {
        text.push_str("# TYPE node_hwmon_temp_celsius gauge\n");
        for sensor in &snapshot.temperatures {
            let _ = writeln!(
                text,
                "node_hwmon_temp_celsius{{chip=\"{}\",sensor=\"{}\"}} {}",
                escape(&sensor.chip),
                escape(&sensor.sensor),
                sensor.celsius
            );
        }
    }
    text
}

/// A label value: backslash, double quote and newline are the only characters the format escapes.
fn escape(value: &str) -> String {
    value.replace('\\', "\\\\").replace('"', "\\\"").replace('\n', "\\n")
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn cpu_lines_are_the_numbered_ones_with_nine_fields_and_a_bad_number_reads_as_zero() {
        let cpus = parse_cpus("cpu  1 2 3 4 5 6 7 8\ncpu0 1 2 3\ncpu1 10 x 30 40 50 60 70 80 0 0\ncpuinfo 1 2 3 4 5 6 7 8 9\nintr 1\n");

        assert_eq!(
            cpus,
            vec![CpuTicks {
                cpu: "1".into(),
                user: 10,
                nice: 0,
                system: 30,
                idle: 40,
                iowait: 50,
                irq: 60,
                softirq: 70,
                steal: 80
            }]
        );
    }

    #[test]
    fn memory_is_read_in_kilobytes_or_bytes_and_in_the_order_of_the_wanted_fields() {
        let memory = parse_memory("MemFree: 5\nMemTotal:  2 kB\nBuffers: nope kB\nShmem: 9 kB\ngarbage\nSwapFree: 7 KB\n");

        assert_eq!(memory, vec![("MemTotal", 2048), ("MemFree", 5), ("SwapFree", 7168)]);
    }

    #[test]
    fn the_exposition_has_only_the_families_portarr_reads() {
        let snapshot = Snapshot {
            cpus: parse_cpus("cpu0 150 0 50 300 0 0 0 0 0 0\n"),
            memory: parse_memory("MemTotal: 16000 kB\nMemAvailable: 8000 kB\nShmem: 5 kB\n"),
            load1: Some(0.5),
            temperatures: vec![Temperature {
                chip: "coretemp".into(),
                sensor: "temp1".into(),
                celsius: 45.0,
            }],
        };

        let text = exposition(&snapshot);
        let mut families: Vec<&str> = text
            .lines()
            .filter(|line| !line.starts_with('#'))
            .map(|line| line.split(['{', ' ']).next().unwrap())
            .collect();
        families.dedup();

        assert_eq!(
            families,
            vec![
                "node_cpu_seconds_total",
                "node_memory_MemTotal_bytes",
                "node_memory_MemAvailable_bytes",
                "node_load1",
                "node_hwmon_temp_celsius"
            ]
        );
        assert!(text.contains("node_cpu_seconds_total{cpu=\"0\",mode=\"idle\"} 3\n"), "{text}");
        assert!(text.contains("node_cpu_seconds_total{cpu=\"0\",mode=\"user\"} 1.5\n"));
        assert!(text.contains("node_memory_MemTotal_bytes 16384000\n"));
        assert!(text.contains("node_load1 0.5\n"));
        assert!(text.contains("node_hwmon_temp_celsius{chip=\"coretemp\",sensor=\"temp1\"} 45\n"));
    }

    #[test]
    fn a_label_value_is_escaped_so_that_it_cannot_break_the_line() {
        let snapshot = Snapshot {
            cpus: vec![],
            memory: vec![],
            load1: None,
            temperatures: vec![Temperature {
                chip: "core\"temp\\\n".into(),
                sensor: "temp1".into(),
                celsius: 61.5,
            }],
        };

        assert!(exposition(&snapshot).contains("chip=\"core\\\"temp\\\\\\n\""));
    }

    #[test]
    fn a_proc_that_is_not_linuxs_is_refused_and_a_machine_without_sensors_is_read() {
        let dir = std::env::temp_dir().join(format!("portarr-reader-host-{}", std::process::id()));
        let _ = fs::remove_dir_all(&dir);
        fs::create_dir_all(&dir).unwrap();
        let (proc_path, sys_path) = (dir.to_str().unwrap(), dir.join("nosys").to_str().unwrap().to_string());

        fs::write(dir.join("stat"), "nothing useful\n").unwrap();
        fs::write(dir.join("meminfo"), "MemTotal: 1 kB\nMemAvailable: 1 kB\n").unwrap();
        assert!(matches!(read_snapshot(proc_path, &sys_path), Err(HostError::Invalid)));

        fs::write(dir.join("stat"), "cpu0 1 2 3 4 5 6 7 8\n").unwrap();
        let snapshot = read_snapshot(proc_path, &sys_path).unwrap();
        assert_eq!(
            (snapshot.load1, snapshot.temperatures.len()),
            (None, 0),
            "no loadavg file and no hwmon directory: still readable"
        );

        fs::remove_file(dir.join("meminfo")).unwrap();
        assert!(matches!(read_snapshot(proc_path, &sys_path), Err(HostError::Io(_))));
        let _ = fs::remove_dir_all(&dir);
    }
}
