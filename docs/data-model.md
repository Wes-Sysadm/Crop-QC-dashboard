# Data Model Notes

This document captures the initial conceptual data model only. Physical schema design will happen later.

## Core Areas

- Users, roles, permissions, and password policy settings.
- Warehouses and rooms.
- Fruit profiles and variety codes.
- Grade list.
- Defect list.
- Starch scale list by fruit profile.
- Apple and pear size conversion thresholds.
- Receipts.
- Receiving samples.
- Fruit QC rows and measurements.
- Fruit defects.
- Photo and attachment metadata.
- QC Summary preview/send/resend state.
- Audit log records.
- Offline sync state for QC Station data.

## Storage Rules

Azure SQL stores structured data and metadata. SharePoint/OneDrive stores photos and attachments. SQL must not store photo binaries.

## Measurement Rules

- Pressure values are recorded in lbs.
- Weight values are recorded in grams.
- Completed fruit rows require Pressure 1, Pressure 2, weight in grams, and grade.
- Starch is per fruit and may be added after initial fruit measurement.
- Starch is required before QC Summary email send.

## Size Conversion Rules

Apple and pear size thresholds are minimum qualifying weights. Assign the largest size category the fruit qualifies for. If below the smallest threshold, mark as Undersized.

## Audit Rules

Create, edit, delete, send, import, and export actions must create audit records.
