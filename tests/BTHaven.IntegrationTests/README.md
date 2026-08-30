# BTHaven integration tests

These tests run against the current Windows machine. They exercise the live `DeviceWatcher`, WASAPI endpoint enumeration, and A2DP service discovery without requiring a physical device to be connected. Positive Bluetooth media, battery, and HFP tests still require explicitly selected hardware.

Raw test/probe output can contain device identifiers, Bluetooth addresses, phone numbers, and endpoint names. Redact those values before publishing results.
