// Drawflow.js interop for Blazor
window.drawflowInterop = {
    editor: null,
    dotnetHelper: null,

    // Initialize Drawflow editor
    initialize: function(containerId, dotnetHelper) {
        const container = document.getElementById(containerId);
        if (!container) {
            console.error('Drawflow container not found:', containerId);
            return false;
        }

        this.dotnetHelper = dotnetHelper;
        this.editor = new Drawflow(container);

        // Configure editor
        this.editor.reroute = true;
        this.editor.reroute_fix_curvature = true;
        this.editor.force_first_input = false;

        // Register node types
        this.registerNodeTypes();

        // Start editor
        this.editor.start();

        // Set up event handlers
        this.setupEventHandlers();

        return true;
    },

    // Register custom node HTML templates
    registerNodeTypes: function() {
        // Trigger node
        this.editor.registerNode('Trigger', this.createNodeHtml('Trigger', '🎯', '#4CAF50'));
        // Classify node
        this.editor.registerNode('Classify', this.createNodeHtml('Classify', '🏷️', '#2196F3'));
        // Query node
        this.editor.registerNode('Query', this.createNodeHtml('Query', '🔍', '#9C27B0'));
        // Validate node
        this.editor.registerNode('Validate', this.createNodeHtml('Validate', '✓', '#FF9800'));
        // Execute node
        this.editor.registerNode('Execute', this.createNodeHtml('Execute', '⚡', '#F44336'));
        // UpdateTicket node
        this.editor.registerNode('UpdateTicket', this.createNodeHtml('Update Ticket', '📝', '#00BCD4'));
        // Notify node
        this.editor.registerNode('Notify', this.createNodeHtml('Notify', '📧', '#E91E63'));
        // Escalate node
        this.editor.registerNode('Escalate', this.createNodeHtml('Escalate', '🚨', '#FF5722'));
        // Condition node
        this.editor.registerNode('Condition', this.createNodeHtml('Condition', '❓', '#795548'));
        // End node
        this.editor.registerNode('End', this.createNodeHtml('End', '🏁', '#607D8B'));
    },

    createNodeHtml: function(name, icon, color) {
        return `
            <div class="workflow-node" style="border-left: 4px solid ${color};">
                <div class="node-header">
                    <span class="node-icon">${icon}</span>
                    <span class="node-title" df-name>${name}</span>
                </div>
                <div class="node-body">
                    <small class="node-type" df-stepType></small>
                </div>
            </div>
        `;
    },

    // Set up Drawflow event handlers
    setupEventHandlers: function() {
        const self = this;

        // Node added
        this.editor.on('nodeCreated', function(nodeId) {
            if (self.dotnetHelper) {
                const node = self.editor.getNodeFromId(nodeId);
                self.dotnetHelper.invokeMethodAsync('OnNodeCreated', nodeId, node.name, node.pos_x, node.pos_y);
            }
        });

        // Node removed
        this.editor.on('nodeRemoved', function(nodeId) {
            if (self.dotnetHelper) {
                self.dotnetHelper.invokeMethodAsync('OnNodeRemoved', nodeId);
            }
        });

        // Node moved
        this.editor.on('nodeMoved', function(nodeId) {
            if (self.dotnetHelper) {
                const node = self.editor.getNodeFromId(nodeId);
                self.dotnetHelper.invokeMethodAsync('OnNodeMoved', nodeId, node.pos_x, node.pos_y);
            }
        });

        // Node selected
        this.editor.on('nodeSelected', function(nodeId) {
            if (self.dotnetHelper) {
                self.dotnetHelper.invokeMethodAsync('OnNodeSelected', nodeId);
            }
        });

        // Connection created
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

        // Connection removed
        this.editor.on('connectionRemoved', function(connection) {
            if (self.dotnetHelper) {
                self.dotnetHelper.invokeMethodAsync('OnConnectionRemoved',
                    connection.output_id,
                    connection.input_id
                );
            }
        });
    },

    // Add a node to the canvas
    addNode: function(stepType, posX, posY, inputs, outputs, data) {
        const nodeId = this.editor.addNode(
            stepType,           // name (used for template lookup)
            inputs,             // number of inputs
            outputs,            // number of outputs
            posX,               // x position
            posY,               // y position
            stepType,           // class
            data,               // data object
            stepType            // html template name
        );
        return nodeId;
    },

    // Remove a node
    removeNode: function(nodeId) {
        this.editor.removeNodeId('node-' + nodeId);
    },

    // Add connection between nodes
    addConnection: function(outputNodeId, inputNodeId, outputIndex, inputIndex) {
        this.editor.addConnection(
            outputNodeId,
            inputNodeId,
            'output_' + outputIndex,
            'input_' + inputIndex
        );
    },

    // Remove connection
    removeConnection: function(outputNodeId, inputNodeId, outputIndex, inputIndex) {
        this.editor.removeSingleConnection(
            outputNodeId,
            inputNodeId,
            'output_' + outputIndex,
            'input_' + inputIndex
        );
    },

    // Update node data
    updateNodeData: function(nodeId, data) {
        this.editor.updateNodeDataFromId(nodeId, data);
    },

    // Get node data
    getNodeData: function(nodeId) {
        return this.editor.getNodeFromId(nodeId);
    },

    // Export entire workflow as JSON
    export: function() {
        return JSON.stringify(this.editor.export());
    },

    // Import workflow from JSON
    import: function(jsonData) {
        const data = JSON.parse(jsonData);
        this.editor.import(data);
    },

    // Clear the canvas
    clear: function() {
        this.editor.clear();
    },

    // Zoom controls
    zoomIn: function() {
        this.editor.zoom_in();
    },

    zoomOut: function() {
        this.editor.zoom_out();
    },

    zoomReset: function() {
        this.editor.zoom_reset();
    },

    // Destroy editor
    destroy: function() {
        if (this.editor) {
            // Drawflow doesn't have explicit destroy, just clear references
            this.editor = null;
            this.dotnetHelper = null;
        }
    }
};
