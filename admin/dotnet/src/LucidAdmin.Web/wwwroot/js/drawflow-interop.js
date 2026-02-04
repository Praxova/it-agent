// Drawflow.js interop for Blazor
window.drawflowInterop = {
    editor: null,
    dotnetHelper: null,
    _initPromise: null,

    // Initialize Drawflow editor with proper timing
    initialize: function(containerId, dotnetHelper) {
        const container = document.getElementById(containerId);
        if (!container) {
            console.error('Drawflow container not found:', containerId);
            return false;
        }

        this.dotnetHelper = dotnetHelper;

        // Create promise that resolves when container has dimensions
        this._initPromise = new Promise((resolve) => {
            const tryInit = () => {
                const rect = container.getBoundingClientRect();
                if (rect.width > 0 && rect.height > 0) {
                    this._doInitialize(container);
                    resolve(true);
                } else {
                    requestAnimationFrame(tryInit);
                }
            };
            // Give Blazor a frame to settle layout
            requestAnimationFrame(tryInit);
        });

        return true;
    },

    _doInitialize: function(container) {
        this.editor = new Drawflow(container);

        // Configure editor
        this.editor.reroute = true;
        this.editor.reroute_fix_curvature = true;
        this.editor.force_first_input = false;

        // Start editor
        this.editor.start();

        // Set up event handlers
        this.setupEventHandlers();

        console.log('Drawflow initialized, container size:',
            container.offsetWidth, 'x', container.offsetHeight);
    },

    // Ensure editor is ready before operations
    _ensureReady: async function() {
        if (this._initPromise) {
            await this._initPromise;
        }
        if (!this.editor) {
            throw new Error('Drawflow editor not initialized');
        }
    },

    // Set up Drawflow event handlers
    setupEventHandlers: function() {
        const self = this;

        this.editor.on('nodeCreated', function(nodeId) {
            if (self.dotnetHelper) {
                const node = self.editor.getNodeFromId(nodeId);
                self.dotnetHelper.invokeMethodAsync('OnNodeCreated',
                    nodeId, node.name, node.pos_x, node.pos_y);
            }
        });

        this.editor.on('nodeRemoved', function(nodeId) {
            if (self.dotnetHelper) {
                self.dotnetHelper.invokeMethodAsync('OnNodeRemoved', nodeId);
            }
        });

        this.editor.on('nodeMoved', function(nodeId) {
            if (self.dotnetHelper) {
                const node = self.editor.getNodeFromId(nodeId);
                self.dotnetHelper.invokeMethodAsync('OnNodeMoved',
                    nodeId, node.pos_x, node.pos_y);
            }
        });

        this.editor.on('nodeSelected', function(nodeId) {
            if (self.dotnetHelper) {
                self.dotnetHelper.invokeMethodAsync('OnNodeSelected', nodeId);
            }
        });

        this.editor.on('connectionCreated', function(connection) {
            if (self.dotnetHelper) {
                self.dotnetHelper.invokeMethodAsync('OnConnectionCreated',
                    connection.output_id,
                    connection.input_id,
                    parseInt(connection.output_class.replace('output_', '')),
                    parseInt(connection.input_class.replace('input_', ''))
                );
            }
        });

        this.editor.on('connectionRemoved', function(connection) {
            if (self.dotnetHelper) {
                self.dotnetHelper.invokeMethodAsync('OnConnectionRemoved',
                    connection.output_id,
                    connection.input_id
                );
            }
        });
    },

    // NEW: Load workflow from structured data (replaces import)
    // steps: [{name, displayName, stepType, positionX, positionY, inputs, outputs}]
    // transitions: [{fromStepName, toStepName, outputIndex, inputIndex}]
    // Returns: {stepNameToNodeId} mapping
    loadWorkflow: async function(steps, transitions) {
        await this._ensureReady();

        // Clear existing content
        this.editor.clear();

        // Suppress events during load to avoid Blazor callback storms
        const savedHelper = this.dotnetHelper;
        this.dotnetHelper = null;

        const stepNameToNodeId = {};

        try {
            // Step type to icon/color mapping (must match registerNodeTypes)
            const stepMeta = {
                'Trigger':      { icon: '🎯', color: '#4CAF50' },
                'Classify':     { icon: '🏷️', color: '#2196F3' },
                'Query':        { icon: '🔍', color: '#9C27B0' },
                'Validate':     { icon: '✓',  color: '#FF9800' },
                'Execute':      { icon: '⚡', color: '#F44336' },
                'UpdateTicket': { icon: '📝', color: '#00BCD4' },
                'Notify':       { icon: '📧', color: '#E91E63' },
                'Escalate':     { icon: '🚨', color: '#FF5722' },
                'Condition':    { icon: '❓', color: '#795548' },
                'End':          { icon: '🏁', color: '#607D8B' }
            };

            // First pass: create all nodes
            for (const step of steps) {
                const meta = stepMeta[step.stepType] || { icon: '📦', color: '#666' };
                const displayName = step.displayName || step.name;

                // Build node HTML matching the registered template style
                const html = `
                    <div class="workflow-node" style="border-left: 4px solid ${meta.color};">
                        <div class="node-header">
                            <span class="node-icon">${meta.icon}</span>
                            <span class="node-title">${displayName}</span>
                        </div>
                        <div class="node-body">
                            <small class="node-type">${step.stepType}</small>
                        </div>
                    </div>
                `;

                const nodeId = this.editor.addNode(
                    step.stepType,          // name (PascalCase, matches registered type)
                    step.inputs || 1,       // number of inputs
                    step.outputs || 1,      // number of outputs
                    step.positionX || 100,  // x position
                    step.positionY || 100,  // y position
                    step.stepType,          // CSS class (PascalCase, matches CSS)
                    {                       // data object
                        name: step.name,
                        stepType: step.stepType,
                        displayName: displayName,
                        configuration: step.configurationJson || '{}'
                    },
                    html                    // custom HTML (not template name)
                );

                stepNameToNodeId[step.name] = nodeId;
            }

            // Small delay to let DOM settle before drawing connections
            await new Promise(resolve => requestAnimationFrame(() =>
                requestAnimationFrame(resolve)));

            // Second pass: create connections
            for (const trans of transitions) {
                const fromId = stepNameToNodeId[trans.fromStepName];
                const toId = stepNameToNodeId[trans.toStepName];

                if (fromId && toId) {
                    try {
                        const outputIdx = (trans.outputIndex || 0) + 1;
                        const inputIdx = (trans.inputIndex || 0) + 1;
                        this.editor.addConnection(
                            fromId, toId,
                            'output_' + outputIdx,
                            'input_' + inputIdx
                        );
                    } catch (e) {
                        console.warn('Failed to add connection:',
                            trans.fromStepName, '->', trans.toStepName, e);
                    }
                } else {
                    console.warn('Missing node for transition:',
                        trans.fromStepName, '->', trans.toStepName);
                }
            }

        } finally {
            // Restore event handler
            this.dotnetHelper = savedHelper;
        }

        return stepNameToNodeId;
    },

    // Add a single node (for drag-and-drop from palette)
    addNode: function(stepType, posX, posY, inputs, outputs, data) {
        if (!this.editor) return -1;

        const stepMeta = {
            'Trigger': { icon: '🎯', color: '#4CAF50' },
            'Classify': { icon: '🏷️', color: '#2196F3' },
            'Query': { icon: '🔍', color: '#9C27B0' },
            'Validate': { icon: '✓', color: '#FF9800' },
            'Execute': { icon: '⚡', color: '#F44336' },
            'UpdateTicket': { icon: '📝', color: '#00BCD4' },
            'Notify': { icon: '📧', color: '#E91E63' },
            'Escalate': { icon: '🚨', color: '#FF5722' },
            'Condition': { icon: '❓', color: '#795548' },
            'End': { icon: '🏁', color: '#607D8B' }
        };

        const meta = stepMeta[stepType] || { icon: '📦', color: '#666' };
        const displayName = (data && data.name) || stepType;

        const html = `
            <div class="workflow-node" style="border-left: 4px solid ${meta.color};">
                <div class="node-header">
                    <span class="node-icon">${meta.icon}</span>
                    <span class="node-title">${displayName}</span>
                </div>
                <div class="node-body">
                    <small class="node-type">${stepType}</small>
                </div>
            </div>
        `;

        const nodeId = this.editor.addNode(
            stepType,
            inputs,
            outputs,
            posX,
            posY,
            stepType,
            data || {},
            html
        );
        return nodeId;
    },

    // Remove a node
    removeNode: function(nodeId) {
        if (this.editor) {
            this.editor.removeNodeId('node-' + nodeId);
        }
    },

    // Add connection between nodes
    addConnection: function(outputNodeId, inputNodeId, outputIndex, inputIndex) {
        if (this.editor) {
            this.editor.addConnection(
                outputNodeId,
                inputNodeId,
                'output_' + outputIndex,
                'input_' + inputIndex
            );
        }
    },

    // Remove connection
    removeConnection: function(outputNodeId, inputNodeId, outputIndex, inputIndex) {
        if (this.editor) {
            this.editor.removeSingleConnection(
                outputNodeId,
                inputNodeId,
                'output_' + outputIndex,
                'input_' + inputIndex
            );
        }
    },

    // Update node data
    updateNodeData: function(nodeId, data) {
        if (this.editor) {
            this.editor.updateNodeDataFromId(nodeId, data);
        }
    },

    // Get node data
    getNodeData: function(nodeId) {
        if (this.editor) {
            return this.editor.getNodeFromId(nodeId);
        }
        return null;
    },

    // Export entire workflow as JSON (for saving LayoutJson backup)
    export: function() {
        if (!this.editor) return '{}';
        return JSON.stringify(this.editor.export());
    },

    // Import workflow from JSON (kept as fallback)
    import: function(jsonData) {
        if (this.editor) {
            const data = JSON.parse(jsonData);
            this.editor.import(data);
        }
    },

    // Clear the canvas
    clear: function() {
        if (this.editor) {
            this.editor.clear();
        }
    },

    // Zoom controls
    zoomIn: function() {
        if (this.editor) this.editor.zoom_in();
    },

    zoomOut: function() {
        if (this.editor) this.editor.zoom_out();
    },

    zoomReset: function() {
        if (this.editor) this.editor.zoom_reset();
    },

    // Destroy editor
    destroy: function() {
        if (this.editor) {
            this.editor = null;
            this.dotnetHelper = null;
            this._initPromise = null;
        }
    }
};
