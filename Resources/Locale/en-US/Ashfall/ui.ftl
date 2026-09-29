ashfall-main-menu-terminal-title = ASHFALL
ashfall-main-menu-address-placeholder = address:port
ashfall-main-menu-status-connecting = Connecting...

ashfall-lobby-terminal-title = ASHFALL
ashfall-lobby-audio-heading = AUDIO SYSTEM
ashfall-lobby-chat-heading = COMMUNICATION CHANNEL
ashfall-lobby-background-title = Ashfall
ashfall-lobby-background-artist = Ashfall project contributors
ashfall-lobby-ready-action = Ready for shift
ashfall-lobby-cancel-ready-action = Cancel readiness
ashfall-lobby-join-shift-action = Start shift
ashfall-lobby-observe-action = Observation mode

ashfall-character-setup-registry-title = ASHEN INDUSTRIAL // PERSONNEL REGISTRY
ashfall-character-setup-file-title = EMPLOYEE RECORD
ashfall-character-setup-status = STATUS: ACTIVE
ashfall-character-setup-last-sync = LAST SYNCHRONIZATION: 10 YEARS AGO
ashfall-character-setup-records-heading = PERSONNEL RECORDS
ashfall-lobby-preview-no-candidate = NO CANDIDATE PINNED, OPEN PERSONNEL FILES
ashfall-personal-files-slot-move-left = Move candidate left
ashfall-personal-files-slot-move-right = Move candidate right

ashfall-options-title = Settings
ui-options-log-actions-in-chat = Log actions and examines in chat
ui-options-log-actions-in-chat-tooltip = Logs item interactions, environmental cues, and examine text directly to chat.
ui-options-coalesce-identical-messages = Coalesce identical messages in chat
ui-options-coalesce-identical-messages-tooltip = Combines identical consecutive messages in chat with a counter.

ashfall-personal-files-title = PERSONNEL ARCHIVE // ASHEN INDUSTRIAL CRYOSTORAGE
ashfall-personal-files-subtitle = Select an employee and confirm their assignment.
ashfall-personal-files-status = STATUS: ACTIVE FILES
ashfall-personal-files-refreshes-left = REFRESHES LEFT: { $count }
ashfall-personal-files-refresh = REQUEST DIFFERENT PERSONNEL FILES
ashfall-personal-files-back = BACK TO LOBBY
ashfall-personal-files-cooldown = Another request is available in { $seconds } s.
ashfall-personal-files-sex-male = Male
ashfall-personal-files-sex-female = Female
ashfall-personal-files-sex-other = Not specified
ashfall-personal-files-card-bio = Age: { $age } | Sex: { $sex }
ashfall-personal-files-dossier-bio = Age: { $age } | Sex: { $sex } | Species: Human
ashfall-personal-files-dossier-label-age = Age
ashfall-personal-files-dossier-label-sex = Sex
ashfall-personal-files-dossier-label-species = Species
ashfall-personal-files-dossier-label-phenotype = Phenotype
ashfall-personal-files-dossier-label-culture = Cultural lineage
ashfall-personal-files-dossier-label-birthplace = Birthplace
ashfall-personal-files-number = PERSONNEL FILE #{ $number }
ashfall-personal-files-confirmed-marker = Candidate confirmed for awakening
ashfall-personal-files-confirmed = CONFIRMED // ASSIGNMENT: { $job }
ashfall-personal-files-assignment-heading = ASSIGNMENT
ashfall-personal-files-assignment-selected = ASSIGNMENT // { $job }
ashfall-personal-files-assignment-help = Only jobs compatible with this employee's qualification are shown.
ashfall-personal-files-no-candidate = Pick an employee from the archive first.
ashfall-personal-files-no-jobs = No assignments are available. This employee cannot be confirmed.
ashfall-personal-files-priority-heading = AWAKENING PRIORITIES
ashfall-personal-files-priority-help = Pick an employee and a job in the ASSIGNMENT block, then pin the pair into slot 1-5; the last two steps work in any order. Slot 1 is checked first.
ashfall-personal-files-slot-help = Awakening priority slot. Press to pin the selected employee; if no job is picked yet, the slot waits for that choice.
ashfall-personal-files-slot-empty = empty
ashfall-personal-files-slot-clear = Remove from priorities
ashfall-personal-files-slot-pending = Slot { $slot } is waiting for a job: pick one in the ASSIGNMENT block.
ashfall-personal-files-slot-no-candidate = Choose an employee first.
ashfall-personal-files-slot-select-role = Pick a job in the ASSIGNMENT block first.
ashfall-personal-files-slot-pinned = Pinned in slot { $slot }.
ashfall-personal-files-pinned-marker = Employee pinned for awakening priorities
ashfall-personal-files-pinned-label = { $confirmed ->
    [true] CONFIRMED // SLOT { $slot } // { $job }
    *[other] PINNED // SLOT { $slot } // { $job }
}
ashfall-personal-files-record-unavailable = ///
ashfall-latejoin-incompatible-job = This job is not compatible with the confirmed personnel file.
