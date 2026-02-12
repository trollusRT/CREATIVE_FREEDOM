# \# CREATIVE\_FREEDOM

# 

# Turn-based roguelike deckbuilder built in Unity featuring color-based cards, fusion mechanics, AP resource management, and status-driven combat.

# 

# ---

# 

# \## Overview

# 

# CREATIVE\_FREEDOM is a combat-focused card battler where the player controls a painter character named Junior. Cards represent paint techniques and colors. Cards can be played normally or fused together to create stronger variants.

# 

# The project is currently focused on implementing and stabilizing the core combat system before expanding into map progression, meta systems, and content.

# 

# ---

# 

# \## Engine Version

# 

# Unity 6  

# 6000.0.42f1

# 

# ---

# 

# \## Repository Structure

# 

# Assets/ — Game code, art, audio, prefabs, animations  

# Packages/ — Unity package configuration  

# ProjectSettings/ — Unity project settings  

# 

# Files intentionally excluded from version control:

# 

# Library/  

# Logs/  

# Build folders  

# Temp files  

# 

# ---

# 

# \## Core Gameplay Loop

# 

# 1\. Turn starts  

# &nbsp;  - Status effects tick  

# &nbsp;  - Dead enemies removed  

# 

# 2\. Player phase  

# &nbsp;  - Player receives AP  

# &nbsp;  - Player plays cards or fuses cards  

# &nbsp;  - Turn ends when AP reaches zero or player passes  

# 

# 3\. Enemy phase  

# &nbsp;  - Enemies act sequentially  

# &nbsp;  - Death and victory checks occur after each action  

# 

# 4\. Turn end  

# &nbsp;  - Hand discarded  

# &nbsp;  - Fusion slots cleared  

# &nbsp;  - Next turn begins  

# 

# ---

# 

# \## Systems

# 

# \### AP System

# \- Player has AP each turn

# \- Cards consume AP

# \- Fusion costs AP

# \- Passing consumes AP

# 

# \### Card Interaction

# Cards support:

# \- Hover lift

# \- Drag targeting

# \- Click fusion selection

# 

# Raycasting uses Physics2D against target colliders.

# 

# \### Effects System

# Visual effects are separated from gameplay logic.

# 

# EffectDirector routes effects to:

# \- Enemy hosts for single target

# \- Mid-screen host for AoE

# 

# Gameplay effects trigger on animation impact frames via callbacks.

# 

# \### Status Effects

# Both player and enemies maintain active effect lists.

# 

# Status effects are processed at turn start.

# 

# ---

# 

# \## Current Feature Status

# 

# Implemented

# \- Card targeting system

# \- AP system

# \- Enemy turn sequencing

# \- Status effect framework

# \- Effect animation system

# \- Fusion selection system

# 

# In Progress

# \- Additional card implementations

# \- Balance tuning

# \- Turn pacing polish

# \- Hover interaction stability

# 

# ---

# 

# \## Branch Workflow

# 

# main — stable baseline  

# dev — active development  

# 

# All work should be done on dev and merged into main only when stable.

# 

# ---

# 

# \## Setup Instructions

# 

# Clone repository: git clone https://github.com/trollusRT/CREATIVE\_FREEDOM.git 



# Open project in Unity Hub using version 6000.0.42f1.

# 

# ---

# 

# \## Development Notes

# 

# When debugging gameplay issues:

# 

# Log state transitions  

# Log raycast hits  

# Log status ticks  

# Log card resolution  

# 

# Avoid modifying collections while iterating. Use snapshots or reverse loops.

# 

# ---

# 

# \## Documentation Files

# 

# The repository root contains project context files for development continuity:

# 

# PROJECT\_SUMMARY.md — overall system overview  

# CURRENT\_TASK.md — active development focus  

# SYSTEM\_MAP.md — script responsibility map  

# BUGLOG.md — tracked issues  

# 

# These files should be kept up to date as systems evolve.

# 

# ---

# 

# \## Contribution Guidelines

# 

# Keep commits small and descriptive.

# 

# Examples: fix: prevent hover pointer exit jitter

# feat: add Toxic Paint card

# refactor: split damage resolution from animation trigger 



# Avoid committing builds or generated files.

# 

# ---

# 

# \## License

# 

# Private project. All rights reserved.







